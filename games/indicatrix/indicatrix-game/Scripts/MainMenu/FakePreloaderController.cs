using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using TMPro;

/// <summary>
/// "Fake preloader" – falošná načítavacia obrazovka, ktorá sa prehrá po kliknutí
/// na New Game ešte pred spustením hernej scény. Štruktúrou je analogická intru:
///   1. Celá obrazovka čierna (fadeOverlay alpha = 1).
///   2. Fade IN z čiernej -> objaví sa GameLogo na čiernom pozadí.
///   3. ~5 sekúnd beží FALOŠNÝ progres 0 → 100 % (nelineárne, náhodné skoky a pauzy).
///      • V strede loga je text "x %".
///      • Logo je najprv celé čiernobiele; podľa percent stúpa "scanline" zdola nahor
///        a pod čiarou sa logo saturuje do plnej farby. Pri 100 % je celé farebné.
///   4. Keď progres dôjde na 100 % (scanline úplne hore), fade OUT do čiernej.
///   5. Zavolá sa onComplete -> načíta sa herná scéna.
///
/// Efekt čiernobiele→farebné cez scanline zabezpečuje shader "UI/LogoScanlineSaturation"
/// (potiahni jeho asset do poľa Scanline Shader). Materiál sa vyrobí za behu.
/// Ak shader nie je priradený, logo ostane farebné (bez efektu) – žiadna chyba.
/// </summary>
public class FakePreloaderController : MonoBehaviour
{
    [Header("References")]
    [Tooltip("Koreň preloadera (čierne pozadie + logo + text + fade overlay). Zapne sa počas preloadera.")]
    [SerializeField] private GameObject preloaderRoot;
    [Tooltip("Image s GameLogo.png v strede. Dostane za behu scanline materiál.")]
    [SerializeField] private Image logoImage;
    [Tooltip("Celoobrazovkový čierny Image NAD všetkým – používa sa na fade in/out.")]
    [SerializeField] private Image fadeOverlay;
    [Tooltip("TMP text v strede loga, zobrazuje 'x %'. Sadne sem TextMeshPro (UI) aj TextMeshPro 3D.")]
    [SerializeField] private TMP_Text percentText;

    [Header("Scanline saturácia")]
    [Tooltip("Shader 'UI/LogoScanlineSaturation' – potiahni sem shader asset.")]
    [SerializeField] private Shader scanlineShader;
    [Range(0f, 0.2f)]
    [Tooltip("Mäkkosť hrany scanline (0 = úplne ostrá čiara).")]
    [SerializeField] private float scanlineSoftness = 0.02f;

    [Header("Časovanie (sekundy)")]
    [SerializeField] private float fadeInDuration = 1.0f;
    [Tooltip("Čistý čas plnenia progresu (bez fade). Celková dĺžka = fade in + toto + fade out.")]
    [SerializeField] private float progressDuration = 5f;
    [SerializeField] private float fadeOutDuration = 1.0f;

    [Header("Falošný progres (percentá)")]
    [Tooltip("Náhodný skok percent na jeden krok – minimum.")]
    [SerializeField] private int minStep = 8;
    [Tooltip("Náhodný skok percent na jeden krok – maximum.")]
    [SerializeField] private int maxStep = 26;
    [Tooltip("Dĺžka nábehu jedného skoku (relatívne, pred normalizáciou na progressDuration).")]
    [SerializeField] private float minRamp = 0.15f;
    [SerializeField] private float maxRamp = 0.6f;
    [Tooltip("Pauza po skoku (relatívne, pred normalizáciou na progressDuration).")]
    [SerializeField] private float minPause = 0.05f;
    [SerializeField] private float maxPause = 0.6f;

    private Material scanlineMat;

    /// <summary>Prehrá preloader a po jeho dokončení (100 % + fade out) zavolá onComplete.</summary>
    public void PlayPreloader(Action onComplete)
    {
        if (preloaderRoot != null) preloaderRoot.SetActive(true);
        StartCoroutine(PreloaderRoutine(onComplete));
    }

    private void EnsureMaterial()
    {
        if (logoImage == null || scanlineShader == null) return;
        if (scanlineMat == null) scanlineMat = new Material(scanlineShader);
        scanlineMat.SetFloat("_Softness", scanlineSoftness);
        logoImage.material = scanlineMat;
    }

    private IEnumerator PreloaderRoutine(Action onComplete)
    {
        EnsureMaterial();

        // Štart: čierna obrazovka, logo čiernobiele, 0 %.
        SetProgress(0f);
        SetOverlayAlpha(1f);

        // Fade IN z čiernej.
        yield return Fade(1f, 0f, fadeInDuration);

        // Falošný náhodný progres 0 -> 100 za ~progressDuration.
        yield return FakeProgressRoutine();

        // Poistka: úplne farebné a 100 %.
        SetProgress(100f);

        // Fade OUT do čiernej.
        yield return Fade(0f, 1f, fadeOutDuration);

        onComplete?.Invoke();
    }

    private IEnumerator FakeProgressRoutine()
    {
        // 1) Vygeneruj náhodné segmenty 0..100 (cieľ, nábeh, pauza).
        List<float> targets = new List<float>();
        List<float> ramps = new List<float>();
        List<float> pauses = new List<float>();

        float p = 0f;
        while (p < 100f)
        {
            p = Mathf.Min(100f, p + UnityEngine.Random.Range(minStep, maxStep + 1));
            targets.Add(p);
            ramps.Add(UnityEngine.Random.Range(minRamp, maxRamp));
            pauses.Add(UnityEngine.Random.Range(minPause, maxPause));
        }

        // 2) Znormalizuj časy tak, aby súčet = progressDuration (nezáleží na počte skokov).
        float sum = 0f;
        for (int i = 0; i < targets.Count; i++) sum += ramps[i] + pauses[i];
        float scale = (sum > 0f) ? progressDuration / sum : 1f;

        // 3) Prehraj: každý skok = plynulý nábeh na cieľ, potom krátka pauza.
        float shown = 0f;
        for (int i = 0; i < targets.Count; i++)
        {
            float start = shown;
            float end = targets[i];
            float dur = ramps[i] * scale;

            float t = 0f;
            while (t < dur)
            {
                t += Time.deltaTime;
                float f = (dur > 0f) ? Mathf.Clamp01(t / dur) : 1f;
                SetProgress(Mathf.Lerp(start, end, f));
                yield return null;
            }
            SetProgress(end);
            shown = end;

            float pause = pauses[i] * scale;
            if (pause > 0f) yield return new WaitForSeconds(pause);
        }
    }

    /// <summary>Nastaví percentá (0-100): text aj výšku scanline saturácie.</summary>
    private void SetProgress(float percent)
    {
        percent = Mathf.Clamp(percent, 0f, 100f);

        if (percentText != null)
            percentText.text = Mathf.RoundToInt(percent) + " %";

        if (scanlineMat != null)
        {
            // Mapovanie tak, aby 0 % = celé čiernobiele a 100 % = celé farebné
            // (aj s ohľadom na mäkkosť hrany).
            float f = percent / 100f;
            float cutoff = Mathf.Lerp(-scanlineSoftness, 1f + scanlineSoftness, f);
            scanlineMat.SetFloat("_Progress", cutoff);
        }
    }

    private IEnumerator Fade(float from, float to, float duration)
    {
        if (duration <= 0f)
        {
            SetOverlayAlpha(to);
            yield break;
        }

        float t = 0f;
        SetOverlayAlpha(from);
        while (t < duration)
        {
            t += Time.deltaTime;
            SetOverlayAlpha(Mathf.Lerp(from, to, t / duration));
            yield return null;
        }
        SetOverlayAlpha(to);
    }

    private void SetOverlayAlpha(float a)
    {
        if (fadeOverlay == null) return;
        Color c = fadeOverlay.color;
        c.a = a;
        fadeOverlay.color = c;
    }
}

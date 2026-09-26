using System;
using System.Collections;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// Plays a self-contained intro inside the Main Menu scene, zložený z jedného alebo
/// viacerých po sebe idúcich screenov. Pre KAŽDÝ sprite v poli 'introSprites' sa
/// prehrá plnohodnotný cyklus:
///   1. Obrazovka je celá čierna.
///   2. Fade IN z čiernej -> ukáže sa obrázok (daný sprite z poľa).
///   3. Hold po dobu 'holdDuration' sekúnd.
///   4. Fade OUT do čiernej.
///   5. Pokračuje sa ďalším spritom v poli (celý cyklus 1-4 znova) atď.
///   6. Po poslednom spritovi sa intro vypne a zavolá sa completion callback (-> Main Menu).
///
/// Poradie prehrávania = poradie prvkov v poli 'introSprites' v Inspectore
/// (element 0 sa prehrá prvý, element 1 druhý, atď.). Ak je pole prázdne, prehrá sa
/// 1x s obrázkom, ktorý je aktuálne nastavený na IntroImage.
///
/// Počas celého intra (ktorýkoľvek zo screenov) môže hráč stlačiť ľubovoľnú klávesu
/// na klávesnici – prehrávanie sa OKAMŽITE ukončí (aj uprostred fade/hold) a preskočí
/// priamo do Main Menu.
///
/// A separate Intro/Loading SCENE is NOT required for this: the whole sequence
/// is a coroutine over an overlay that gets disabled at the end, so it stays in
/// the Main Menu scene without any loading cost. (See SETUP_GUIDE.md, section 10.)
/// </summary>
public class IntroSequenceController : MonoBehaviour
{
    [Header("References")]
    [Tooltip("Parent object containing the intro image + black overlay. Disabled when the intro ends.")]
    [SerializeField] private GameObject introRoot;
    [Tooltip("Image komponent na objekte IntroImage – na ňom sa budú striedať obrázky.")]
    [SerializeField] private Image introImage;
    [Tooltip("Full-screen black Image used for fading. Must be on TOP of the intro image.")]
    [SerializeField] private Image fadeOverlay;

    [Header("Images")]
    [Tooltip("Obrázky, ktoré sa prehrajú za sebou (každý = fade in → hold → fade out). Sem natiahni 2 rôzne sprity. Ak ostane prázdne, prehrá sa 1x aktuálny sprite na IntroImage.")]
    [SerializeField] private Sprite[] introSprites;

    [Header("Timing (seconds)")]
    [SerializeField] private float fadeInDuration = 1.5f;
    [SerializeField] private float holdDuration = 5f;
    [SerializeField] private float fadeOutDuration = 1.5f;

    [Header("Skip")]
    [Tooltip("Ak je zapnuté, stlačenie ľubovoľnej klávesy počas intra ho okamžite preskočí do Main Menu.")]
    [SerializeField] private bool allowSkip = true;

    // Beží práve intro? Len vtedy reagujeme na klávesy.
    private bool isPlaying = false;
    // Požiadavka na okamžité preskočenie.
    private bool skipRequested = false;

    /// <summary>Plays the intro, then calls onComplete.</summary>
    public void PlayIntro(Action onComplete)
    {
        if (introRoot != null) introRoot.SetActive(true);
        StartCoroutine(IntroRoutine(onComplete));
    }

    /// <summary>
    /// Okamžite skryje intro BEZ prehrávania (žiadna coroutine, žiadny fade).
    /// Použi vtedy, keď sa intro v tomto behu hry už raz odohralo (napr. návrat
    /// z hry cez BackToMainMenu) a scéna MainMenu sa načítava znova – introRoot
    /// môže byť v scéne uložený ako aktívny, tak ho treba explicitne vypnúť,
    /// inak by "prekukol" navrchu obrazovky, hoci PlayIntro() sa vôbec nezavolal.
    /// </summary>
    public void SkipImmediate()
    {
        isPlaying = false;
        skipRequested = false;
        if (introRoot != null) introRoot.SetActive(false);
    }

    private void Update()
    {
        // Počas prehrávania intra: akákoľvek klávesa = okamžité preskočenie.
        // Input.anyKeyDown reaguje aj na tlačidlá myši; ak chceš LEN klávesnicu,
        // pozri poznámku pod skriptom.
        if (isPlaying && allowSkip && Input.anyKeyDown)
            skipRequested = true;
    }

    private IEnumerator IntroRoutine(Action onComplete)
    {
        isPlaying = true;
        skipRequested = false;

        // Počet prebehov = počet spritov v poli; ak je pole prázdne, prebehne 1x
        // s obrázkom, ktorý je aktuálne nastavený na IntroImage.
        int passes = (introSprites != null && introSprites.Length > 0)
            ? introSprites.Length
            : 1;

        // Prehraj VŠETKY spritý v poli, v poradí ako sú v Inspectore (element 0 prvý,
        // element 1 druhý, ...) – každý dostane svoj vlastný fade in / hold / fade out.
        for (int i = 0; i < passes; i++)
        {
            if (skipRequested) break;

            // Kým je overlay čierny, prepni obrázok pre tento prebeh.
            if (introSprites != null && introSprites.Length > 0
                && introImage != null && introSprites[i] != null)
            {
                introImage.sprite = introSprites[i];
            }

            // Start fully black (platí aj medzi jednotlivými prebehmi).
            SetOverlayAlpha(1f);

            // Fade in from black.
            yield return Fade(1f, 0f, fadeInDuration);
            if (skipRequested) break;

            // Hold the image (prerušiteľné klávesou).
            yield return HoldOrSkip(holdDuration);
            if (skipRequested) break;

            // Fade out to black.
            yield return Fade(0f, 1f, fadeOutDuration);
        }

        isPlaying = false;

        // Hide intro and reveal the menu (aj pri preskočení – ideme rovno do menu).
        if (introRoot != null) introRoot.SetActive(false);
        onComplete?.Invoke();
    }

    /// <summary>Čaká 'duration' sekúnd, ale pri požiadavke na skip skončí okamžite.</summary>
    private IEnumerator HoldOrSkip(float duration)
    {
        float t = 0f;
        while (t < duration)
        {
            if (skipRequested) yield break;
            t += Time.deltaTime;
            yield return null;
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
            if (skipRequested) yield break; // pri skipe okamžite prestaň fadovať
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
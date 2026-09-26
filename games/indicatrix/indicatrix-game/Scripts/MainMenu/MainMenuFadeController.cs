using System.Collections;
using UnityEngine;

/// <summary>
/// Fades the main panel in over a configurable duration (default 3s) using a CanvasGroup.
/// Attach this to the SAME GameObject that has the CanvasGroup (MainPanel).
/// </summary>
[RequireComponent(typeof(CanvasGroup))]
public class MainMenuFadeController : MonoBehaviour
{
    [Tooltip("Duration of the fade-in in seconds.")]
    [SerializeField] private float fadeDuration = 3f;

    private CanvasGroup canvasGroup;
    private Coroutine fadeRoutine;

    private void Awake()
    {
        canvasGroup = GetComponent<CanvasGroup>();
        canvasGroup.alpha = 0f;
        canvasGroup.interactable = false;
        canvasGroup.blocksRaycasts = false;
    }

    /// <summary>Starts the fade-in from alpha 0 to 1.</summary>
    public void PlayFadeIn()
    {
        if (!gameObject.activeInHierarchy) return;
        if (fadeRoutine != null) StopCoroutine(fadeRoutine);
        fadeRoutine = StartCoroutine(FadeInRoutine());
    }

    /// <summary>Immediately makes the panel fully visible and interactive (no animation).</summary>
    public void SetVisibleInstant()
    {
        if (fadeRoutine != null)
        {
            StopCoroutine(fadeRoutine);
            fadeRoutine = null;
        }
        if (canvasGroup == null) canvasGroup = GetComponent<CanvasGroup>();
        canvasGroup.alpha = 1f;
        canvasGroup.interactable = true;
        canvasGroup.blocksRaycasts = true;
    }

    private IEnumerator FadeInRoutine()
    {
        canvasGroup.interactable = false;
        canvasGroup.blocksRaycasts = false;
        canvasGroup.alpha = 0f;

        float t = 0f;
        while (t < fadeDuration)
        {
            t += Time.deltaTime;
            canvasGroup.alpha = Mathf.Clamp01(t / fadeDuration);
            yield return null;
        }

        canvasGroup.alpha = 1f;
        canvasGroup.interactable = true;
        canvasGroup.blocksRaycasts = true;
        fadeRoutine = null;
    }
}

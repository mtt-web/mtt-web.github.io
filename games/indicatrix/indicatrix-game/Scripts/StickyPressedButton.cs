using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// StickyPressedButton
/// ─────────────────────────────────────────────────────────────────────────
/// Pomocná (NE-MonoBehaviour) trieda, ktorá vie Unity Button "zaseknúť"
/// vo vizuálnom stave Pressed a znovu ho vrátiť do Default stavu.
///
/// PRINCÍP:
///   Unity Button nemá trvalý "zatlačený" stav – Pressed Sprite sa ukáže len
///   počas držania myši. Preto pri zatlačení:
///     • Image.sprite (základný / Default sprite)  → Pressed Sprite
///     • SpriteState.highlightedSprite             → Pressed Sprite
///     • SpriteState.selectedSprite                → Pressed Sprite
///   takže button vyzerá zatlačený vo VŠETKÝCH stavoch (aj pri hoveri).
///   Pri odtlačení sa obnovia pôvodné hodnoty zapamätané z Inspectora.
///
///   Priradenie Button.spriteState interne volá prekreslenie aktuálneho
///   stavu, takže zmena je viditeľná okamžite (bez čakania na pohyb myši).
///
/// POŽIADAVKY (Inspector):
///   • Transition = Sprite Swap
///   • Target Graphic = Image buttonu (štandardne je)
///   • Pressed Sprite = vyplnený
///   Selected Sprite môže ostať prázdny – skript si ho rieši sám.
///
/// Používajú ju RailConstructionMenuUI a RoadConstructionMenuUI.
/// ─────────────────────────────────────────────────────────────────────────
/// </summary>
public sealed class StickyPressedButton
{
    public Button Button { get; }

    private readonly Image _image;
    private readonly Sprite _defaultSprite;
    private readonly SpriteState _defaultSpriteState;
    private readonly Sprite _pressedSprite;

    private bool _isPressed;

    public StickyPressedButton(Button button)
    {
        Button = button;
        _image = button.image; // = targetGraphic as Image

        // Zapamätáme si pôvodný vzhľad z Inspectora (Default + SpriteState).
        _defaultSpriteState = button.spriteState;
        _pressedSprite = _defaultSpriteState.pressedSprite;
        _defaultSprite = _image != null ? _image.sprite : null;

        if (_image == null)
            Debug.LogWarning($"[StickyPressedButton] '{button.name}': Target Graphic nie je Image – zatlačený stav nebude viditeľný.", button);
        else if (_pressedSprite == null)
            Debug.LogWarning($"[StickyPressedButton] '{button.name}': Pressed Sprite nie je nastavený v Inspectore.", button);
    }

    /// <summary>
    /// Nastaví vizuálny stav buttonu. Idempotentné – ak sa stav nemení,
    /// nerobí nič.
    /// </summary>
    public void SetPressed(bool pressed)
    {
        if (_isPressed == pressed) return;
        _isPressed = pressed;

        if (_image == null || _pressedSprite == null) return;

        if (pressed)
        {
            _image.sprite = _pressedSprite;

            SpriteState s = _defaultSpriteState;
            s.highlightedSprite = _pressedSprite;
            s.selectedSprite = _pressedSprite;
            Button.spriteState = s;
        }
        else
        {
            _image.sprite = _defaultSprite;
            Button.spriteState = _defaultSpriteState;
        }
    }
}

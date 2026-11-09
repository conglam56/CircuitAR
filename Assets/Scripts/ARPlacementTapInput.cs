using System.Collections.Generic;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;
#if ENABLE_INPUT_SYSTEM
using UnityEngine.InputSystem;
#endif

/// <summary>A short camera-area tap; UI, swipes and multi-touch never place objects.</summary>
public sealed class ARPlacementTapInput
{
    private readonly List<RaycastResult> uiHits = new List<RaycastResult>(16);
    private PointerEventData pointerEvent;
    private EventSystem pointerEventSystem;
    private bool eligible;
    private Vector2 beganAt;
    private float beganTime;
    private int pointerId;
    public Vector2 Position { get; private set; }
    public bool Confirmed { get; private set; }

    public bool Read(out Vector2 position, out bool confirmed)
    {
        Confirmed = false;
        bool down = false, held = false, up = false, cancelled = false;
        int id = 0;
        Vector2 point = Position;
#if ENABLE_INPUT_SYSTEM
        var touchscreen = Touchscreen.current;
        if (touchscreen != null)
        {
            var touch = touchscreen.primaryTouch;
            point = touch.position.ReadValue();
            id = touch.touchId.ReadValue();
            down = touch.press.wasPressedThisFrame;
            held = touch.press.isPressed;
            up = touch.press.wasReleasedThisFrame;
            cancelled = touch.phase.ReadValue() == UnityEngine.InputSystem.TouchPhase.Canceled;
            int touches = 0;
            foreach (var activeTouch in touchscreen.touches)
                if (activeTouch.press.isPressed) touches++;
            if (touches > 1) cancelled = true;
        }
#if UNITY_EDITOR
        else if (Mouse.current != null)
        {
            point = Mouse.current.position.ReadValue();
            down = Mouse.current.leftButton.wasPressedThisFrame;
            held = Mouse.current.leftButton.isPressed;
            up = Mouse.current.leftButton.wasReleasedThisFrame;
        }
#endif
#elif ENABLE_LEGACY_INPUT_MANAGER
        if (Input.touchCount == 1)
        {
            Touch touch = Input.GetTouch(0);
            point = touch.position;
            id = touch.fingerId;
            down = touch.phase == TouchPhase.Began;
            up = touch.phase == TouchPhase.Ended;
            cancelled = touch.phase == TouchPhase.Canceled;
            held = !up && !cancelled;
        }
        else if (Input.touchCount > 1) cancelled = true;
#endif
        bool active = Process(point, id, down, held, up, cancelled);
        position = Position;
        confirmed = Confirmed;
        return active;
    }

    private bool Process(Vector2 point, int id, bool down, bool held, bool up, bool cancelled)
    {
        Confirmed = false;
        if (down)
        {
            beganAt = point;
            beganTime = Time.unscaledTime;
            pointerId = id;
            eligible = !IsOverUI(point);
        }
        float maxMotion = Mathf.Max(18f, Mathf.Min(Screen.width, Screen.height) * 0.025f);
        if (cancelled || id != pointerId || (point - beganAt).sqrMagnitude > maxMotion * maxMotion ||
            Time.unscaledTime - beganTime > 0.4f)
            eligible = false;
        if (eligible && (held || up)) Position = point;
        if (up)
        {
            Confirmed = eligible && !IsOverUI(point);
            eligible = false;
            return Confirmed;
        }
        if (!held) eligible = false;
        return eligible;
    }

    private bool IsOverUI(Vector2 point)
    {
        EventSystem events = EventSystem.current;
        if (events == null) return false;
        if (pointerEventSystem != events)
        {
            pointerEventSystem = events;
            pointerEvent = new PointerEventData(events);
        }
        pointerEvent.Reset();
        pointerEvent.position = point;
        uiHits.Clear();
        events.RaycastAll(pointerEvent, uiHits);
        for (int i = 0; i < uiHits.Count; i++)
            if (uiHits[i].module is GraphicRaycaster) return true;
        return false;
    }

    public void Reset()
    {
        eligible = false;
        Confirmed = false;
        uiHits.Clear();
    }
}

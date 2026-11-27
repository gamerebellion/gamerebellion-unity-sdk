using UnityEngine.UIElements;

namespace GameRebellionSdk.Unity.Editor
{
    /// <summary>What a status dot shows. Idle is a hollow dot: not running, or unknown.</summary>
    internal enum GRStatus
    {
        Idle,
        Ok,
        Warn,
        Error,
    }

    internal enum GRMessageKind
    {
        Info,
        Ok,
        Warn,
        Error,
    }

    /// <summary>A one-line notice shown under a section's controls.</summary>
    internal sealed class GRMessage
    {
        public GRMessage(string text, GRMessageKind kind = GRMessageKind.Info)
        {
            Text = text;
            Kind = kind;
        }

        public string Text { get; }
        public GRMessageKind Kind { get; }
    }

    /// <summary>
    /// Class names the C# side toggles. State is shown by switching classes, never
    /// by writing styles: the USS decides what each state looks like.
    /// </summary>
    internal static class GRUi
    {
        public const string Hidden = "gr-hidden";
        public const string Narrow = "gr-root--narrow";
        public const string Field = "gr-field";
        public const string MockOnly = "gr-mock-only";

        public static void SetHidden(VisualElement element, bool hidden)
        {
            element.EnableInClassList(Hidden, hidden);
        }

        public static void ShowMessage(Label label, GRMessage message)
        {
            SetHidden(label, message == null);
            label.text = message?.Text ?? string.Empty;
            label.EnableInClassList("gr-message--ok", message?.Kind == GRMessageKind.Ok);
            label.EnableInClassList("gr-message--warn", message?.Kind == GRMessageKind.Warn);
            label.EnableInClassList("gr-message--error", message?.Kind == GRMessageKind.Error);
        }

        public static void SetStatus(VisualElement dot, GRStatus status)
        {
            dot.EnableInClassList("gr-status-dot--ok", status == GRStatus.Ok);
            dot.EnableInClassList("gr-status-dot--warn", status == GRStatus.Warn);
            dot.EnableInClassList("gr-status-dot--error", status == GRStatus.Error);
        }
    }
}

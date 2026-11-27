using UnityEngine.UIElements;

namespace GameRebellionSdk.Unity.Editor
{
    /// <summary>The branded sign-in screen shown while signed out (GRSignInView.uxml).</summary>
    internal sealed class GRSignInView : IGRSection
    {
        private readonly GRWindowModel _model;
        private readonly VisualElement _idle;
        private readonly VisualElement _busy;
        private readonly Label _message;
        private readonly Label _storageNotice;

        public GRSignInView(VisualElement root, GRWindowModel model)
        {
            _model = model;
            _idle = root.Q("signin-idle");
            _busy = root.Q("signin-busy");
            _message = root.Q<Label>("signin-message");
            _storageNotice = root.Q<Label>("signin-storage-notice");

            root.Q<Button>("sign-in-button").clicked += model.SignIn;
            root.Q<Button>("cancel-sign-in-button").clicked += model.CancelSignIn;

            if (model.IsAuthSimulated)
            {
                root.Q<Label>("signing-in-hint").text = "Signing in with a test account…";
            }
        }

        public void Render()
        {
            var signingIn = _model.AuthPhase == GRAuthPhase.SigningIn;
            GRUi.SetHidden(_idle, signingIn);
            GRUi.SetHidden(_busy, !signingIn);
            GRUi.ShowMessage(_message, _model.AccountMessage);
            GRUi.ShowMessage(_storageNotice, _model.StorageNotice);
        }
    }
}

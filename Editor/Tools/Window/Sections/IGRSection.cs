namespace GameRebellionSdk.Unity.Editor
{
    /// <summary>
    /// A controller for one UXML section of the window. It queries its elements
    /// once, wires their callbacks to the model, and in <see cref="Render"/> only
    /// sets text, toggles classes and enables or disables controls.
    /// </summary>
    internal interface IGRSection
    {
        void Render();
    }
}

using CardboardXR.Demos;
using CardboardXR.Demos.PcStream;
using UnityEditor;
using UnityEngine;

namespace ProjectBootstrap
{
    // PC Stream: the screen that shows the PC, and the demo that runs its panel (built at runtime).
    class PcStreamSetup : DemoSetup
    {
        public override string Name => "PcStream";
        public override int Order => 60;

        public override DemoCatalog.Entry Entry => new()
        {
            Id = "pc-stream",
            Title = "PC Stream",
            Description = "Play your PC's games, such as Steam Big Picture, on a big screen in the headset, with a Bluetooth controller. Needs Sunshine running on the PC, on the same Wi-Fi.",
            Tagline = "Your PC's games on a big screen, with a controller",
            Icon = "sports_esports",
            Tint = new Color(0.16f, 0.4f, 0.62f),
            // Passthrough on (and 6DoF with it, which passthrough needs), so the screen hangs in your room
            // and stays put as you move. Hands off: the controller does the playing, and hands are most of the heat.
            Requirements = new DemoRequirements { SixDof = Need.On, Passthrough = Need.On, Hands = Need.Off },
        };

        public override void BuildScene(string folder)
        {
            // White, so the stream's own colors show; unlit, so the room's light doesn't tint a screen.
            var material = StereoBuilder.CreateMaterial("PC Screen", DemoAssets.Unlit, Color.white, null, 1f, folder);

            var screen = new GameObject("PC Screen").AddComponent<StreamScreen>();
            var serializedScreen = new SerializedObject(screen);
            serializedScreen.FindProperty("m_Material").objectReferenceValue = material;
            serializedScreen.ApplyModifiedPropertiesWithoutUndo();

            var demo = new GameObject("PC Stream Demo").AddComponent<PcStreamDemo>();
            var serializedDemo = new SerializedObject(demo);
            serializedDemo.FindProperty("m_Screen").objectReferenceValue = screen;
            serializedDemo.ApplyModifiedPropertiesWithoutUndo();
        }
    }
}

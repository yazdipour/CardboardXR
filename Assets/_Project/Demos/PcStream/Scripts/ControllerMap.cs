using UnityEngine;

namespace CardboardXR.Demos.PcStream
{
    // A controller's state, read from whatever the platform gives (see NativeStreamSource), in plain
    // fields so the mapping to what the PC wants can be tested.
    public struct ControllerInput
    {
        public bool South, East, West, North;
        public bool DpadUp, DpadDown, DpadLeft, DpadRight;
        public bool LeftShoulder, RightShoulder;
        public bool LeftStick, RightStick;
        public bool Start, Select;
        // 0 to 1.
        public float LeftTrigger, RightTrigger;
        // -1 to 1, up positive.
        public Vector2 LeftStickAxis, RightStickAxis;
    }

    // Moonlight's controller packet: the PC sees an Xbox controller, so South is A, East is B, and so on.
    public struct ControllerPacket : System.IEquatable<ControllerPacket>
    {
        public uint Buttons;
        public byte LeftTrigger, RightTrigger;
        public short LeftX, LeftY, RightX, RightY;

        public bool Equals(ControllerPacket other) =>
            Buttons == other.Buttons && LeftTrigger == other.LeftTrigger && RightTrigger == other.RightTrigger &&
            LeftX == other.LeftX && LeftY == other.LeftY && RightX == other.RightX && RightY == other.RightY;

        public override bool Equals(object obj) => obj is ControllerPacket other && Equals(other);

        public override int GetHashCode() => System.HashCode.Combine(Buttons, LeftTrigger, RightTrigger, LeftX, LeftY, RightX, RightY);
    }

    public static class ControllerMap
    {
        // Moonlight's button flags (Limelight.h).
        public const uint Up = 0x0001, Down = 0x0002, Left = 0x0004, Right = 0x0008;
        public const uint Play = 0x0010, Back = 0x0020, LeftStickClick = 0x0040, RightStickClick = 0x0080;
        public const uint LeftBumper = 0x0100, RightBumper = 0x0200, Guide = 0x0400;
        public const uint A = 0x1000, B = 0x2000, X = 0x4000, Y = 0x8000;

        // Start and Select together make the Guide button (Steam's menu), which most controllers
        // that reach a phone don't have a button for.
        public static ControllerPacket Map(in ControllerInput input)
        {
            uint buttons = 0;
            if (input.South) buttons |= A;
            if (input.East) buttons |= B;
            if (input.West) buttons |= X;
            if (input.North) buttons |= Y;
            if (input.DpadUp) buttons |= Up;
            if (input.DpadDown) buttons |= Down;
            if (input.DpadLeft) buttons |= Left;
            if (input.DpadRight) buttons |= Right;
            if (input.LeftShoulder) buttons |= LeftBumper;
            if (input.RightShoulder) buttons |= RightBumper;
            if (input.LeftStick) buttons |= LeftStickClick;
            if (input.RightStick) buttons |= RightStickClick;
            if (input.Start && input.Select)
                buttons |= Guide;
            else if (input.Start)
                buttons |= Play;
            else if (input.Select)
                buttons |= Back;

            return new ControllerPacket
            {
                Buttons = buttons,
                LeftTrigger = Trigger(input.LeftTrigger),
                RightTrigger = Trigger(input.RightTrigger),
                LeftX = Axis(input.LeftStickAxis.x),
                LeftY = Axis(input.LeftStickAxis.y),
                RightX = Axis(input.RightStickAxis.x),
                RightY = Axis(input.RightStickAxis.y),
            };
        }

        static byte Trigger(float value) => (byte)Mathf.RoundToInt(Mathf.Clamp01(value) * 255f);

        static short Axis(float value) => (short)Mathf.RoundToInt(Mathf.Clamp(value, -1f, 1f) * 32767f);
    }
}

using NUnit.Framework;
using UnityEngine;

namespace CardboardXR.Demos.PcStream.Tests
{
    public class ControllerMapTests
    {
        [Test]
        public void FaceButtonsAreAnXboxPad()
        {
            var packet = ControllerMap.Map(new ControllerInput { South = true, East = true, West = true, North = true });
            Assert.AreEqual(0x1000u | 0x2000u | 0x4000u | 0x8000u, packet.Buttons);
        }

        [Test]
        public void StartAndSelectAloneAreThemselvesButTogetherAreGuide()
        {
            Assert.AreEqual(ControllerMap.Play, ControllerMap.Map(new ControllerInput { Start = true }).Buttons);
            Assert.AreEqual(ControllerMap.Back, ControllerMap.Map(new ControllerInput { Select = true }).Buttons);
            Assert.AreEqual(ControllerMap.Guide, ControllerMap.Map(new ControllerInput { Start = true, Select = true }).Buttons);
        }

        [Test]
        public void ShouldersDpadAndStickClicksKeepTheirOwnBits()
        {
            var packet = ControllerMap.Map(new ControllerInput
            {
                DpadUp = true, DpadRight = true, LeftShoulder = true, RightStick = true,
            });
            Assert.AreEqual(ControllerMap.Up | ControllerMap.Right | ControllerMap.LeftBumper | ControllerMap.RightStickClick, packet.Buttons);
        }

        [Test]
        public void SticksAndTriggersUseTheFullRange()
        {
            var packet = ControllerMap.Map(new ControllerInput
            {
                LeftStickAxis = new Vector2(1f, -1f), RightStickAxis = new Vector2(-1f, 0.5f), LeftTrigger = 1f, RightTrigger = 0.5f,
            });
            Assert.AreEqual(32767, packet.LeftX);
            Assert.AreEqual(-32767, packet.LeftY);
            Assert.AreEqual(-32767, packet.RightX);
            Assert.AreEqual(16384, packet.RightY, 1);
            Assert.AreEqual(255, packet.LeftTrigger);
            Assert.AreEqual(128, packet.RightTrigger, 1);
        }

        [Test]
        public void ValuesOutOfRangeAreClamped()
        {
            var packet = ControllerMap.Map(new ControllerInput { LeftStickAxis = new Vector2(5f, -5f), LeftTrigger = 3f, RightTrigger = -1f });
            Assert.AreEqual(32767, packet.LeftX);
            Assert.AreEqual(-32767, packet.LeftY);
            Assert.AreEqual(255, packet.LeftTrigger);
            Assert.AreEqual(0, packet.RightTrigger);
        }

        [Test]
        public void AnIdleControllerIsAllZero()
        {
            Assert.AreEqual(default(ControllerPacket), ControllerMap.Map(default));
        }
    }
}

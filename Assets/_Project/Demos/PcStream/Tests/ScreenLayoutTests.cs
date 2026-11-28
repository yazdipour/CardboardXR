using NUnit.Framework;
using UnityEngine;

namespace CardboardXR.Demos.PcStream.Tests
{
    public class ScreenLayoutTests
    {
        [Test]
        public void EachStepScalesTheWidthByTheSameFactor()
        {
            var width = ScreenLayout.DefaultWidth;
            Assert.AreEqual(width * ScreenLayout.Step, ScreenLayout.Resize(width, 1), 1e-4f);
            Assert.AreEqual(width / ScreenLayout.Step, ScreenLayout.Resize(width, -1), 1e-4f);
            // Back and forth comes home.
            Assert.AreEqual(width, ScreenLayout.Resize(ScreenLayout.Resize(width, 2), -2), 1e-4f);
        }

        [Test]
        public void TheWidthStaysInRange()
        {
            Assert.AreEqual(ScreenLayout.MaxWidth, ScreenLayout.Resize(ScreenLayout.MaxWidth, 1), 1e-4f);
            Assert.AreEqual(ScreenLayout.MinWidth, ScreenLayout.Resize(ScreenLayout.MinWidth, -1), 1e-4f);
            Assert.AreEqual(ScreenLayout.MaxWidth, ScreenLayout.Resize(ScreenLayout.DefaultWidth, 50), 1e-4f);
        }

        [Test]
        public void TheDefaultSizeIsInsideTheRange()
        {
            Assert.GreaterOrEqual(ScreenLayout.DefaultWidth, ScreenLayout.MinWidth);
            Assert.LessOrEqual(ScreenLayout.DefaultWidth, ScreenLayout.MaxWidth);
        }

        [Test]
        public void HeightFollowsTheAspect()
        {
            Assert.AreEqual(1.8f, ScreenLayout.Height(3.2f, 16f / 9f), 1e-4f);
            Assert.AreEqual(2f, ScreenLayout.Height(2f, 1f), 1e-4f);
        }

        [Test]
        public void AngularWidthIsRightForKnownCases()
        {
            // A screen as wide as the distance fills 2*atan(0.5) = 53.13 degrees.
            Assert.AreEqual(53.13f, ScreenLayout.AngularWidth(3f, 3f), 0.01f);
            Assert.Greater(ScreenLayout.AngularWidth(ScreenLayout.MaxWidth), ScreenLayout.AngularWidth(ScreenLayout.MinWidth));
        }

        [Test]
        public void AspectComesFromTheFrameOrFallsBack()
        {
            Assert.AreEqual(2f, ScreenLayout.Aspect(new Vector2Int(2000, 1000)), 1e-4f);
            Assert.AreEqual(ScreenLayout.DefaultAspect, ScreenLayout.Aspect(default), 1e-4f);
        }
    }
}

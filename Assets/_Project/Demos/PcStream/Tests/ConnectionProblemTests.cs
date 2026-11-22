using NUnit.Framework;

namespace CardboardXR.Demos.PcStream.Tests
{
    public class ConnectionProblemTests
    {
        [TestCase("The request timed out.")]
        [TestCase("Could not connect to the server.")]
        [TestCase("Host is unreachable")]
        [TestCase("The Internet connection appears to be offline.")]
        public void NetworkErrorsSayTheAddressOrWifiIsWrong(string status)
        {
            var problem = ConnectionProblem.Explain(status, "192.168.0.142");
            Assert.AreEqual("Can't reach the PC", problem.Title);
            StringAssert.Contains("192.168.0.142", problem.Message);
            StringAssert.Contains(status, problem.Message);
        }

        [Test]
        public void AWrongPinIsNamed()
        {
            Assert.AreEqual("Wrong PIN", ConnectionProblem.Explain("Incorrect PIN", "pc").Title);
        }

        [Test]
        public void AMissingAppPointsAtSunshinesApplications()
        {
            var problem = ConnectionProblem.Explain("No \"Steam Big Picture\" app in Sunshine", "10.0.0.5");
            Assert.AreEqual("No Steam in Sunshine", problem.Title);
            StringAssert.Contains("10.0.0.5:47990", problem.Message);
        }

        [Test]
        public void AFailedStageIsAStreamProblem()
        {
            Assert.AreEqual("The stream wouldn't start", ConnectionProblem.Explain("RTSP handshake failed (error -1)", "pc").Title);
        }

        [Test]
        public void UnknownTextIsShownAsItCame()
        {
            var problem = ConnectionProblem.Explain("Something odd", "pc");
            Assert.AreEqual("Couldn't connect", problem.Title);
            Assert.AreEqual("Something odd", problem.Message);
        }

        [Test]
        public void NoStatusAndNoHostStillMakeSense()
        {
            var problem = ConnectionProblem.Explain(null, null);
            Assert.IsNotEmpty(problem.Title);
            Assert.IsNotEmpty(problem.Message);
        }
    }
}

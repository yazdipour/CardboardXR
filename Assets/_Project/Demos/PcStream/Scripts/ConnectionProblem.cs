using System;

namespace CardboardXR.Demos.PcStream
{
    // Why a connection failed, in words for someone wearing a headset: a short title and what to try.
    // The plugin's status text (a system error, or Sunshine's own message) is what's explained; text this
    // doesn't know is shown as it came.
    public readonly struct ConnectionProblem
    {
        public readonly string Title;
        public readonly string Message;

        ConnectionProblem(string title, string message)
        {
            Title = title;
            Message = message;
        }

        public static ConnectionProblem Explain(string status, string host)
        {
            status = string.IsNullOrWhiteSpace(status) ? "No reason was given." : status.Trim();
            var pc = string.IsNullOrWhiteSpace(host) ? "the PC" : host.Trim();

            if (Has(status, "timed out", "unreachable", "could not connect", "no route", "offline", "connection was lost",
                    "hostname could not be found", "couldn't reach", "refused"))
                return new ConnectionProblem("Can't reach the PC",
                    $"Is {pc} the PC's address? Check that Sunshine is running there and that this phone is on the same Wi-Fi. " +
                    $"If the app runs in LiveContainer, allow Local Network for LiveContainer in iOS Settings.\n({status})");

            if (Has(status, "incorrect pin"))
                return new ConnectionProblem("Wrong PIN", "The PIN typed into Sunshine didn't match. Tap Try again for a new one.");

            if (Has(status, "declined", "already in progress"))
                return new ConnectionProblem("Pairing didn't finish",
                    "Sunshine's PIN page wasn't filled in, or another pairing was still going. Tap Try again and enter the new PIN.");

            if (Has(status, "no longer knows"))
                return new ConnectionProblem("Pair again", "The PC has forgotten this phone. Tap Try again to pair once more.");

            if (Has(status, "already has this phone paired"))
                return new ConnectionProblem("Pairing needs a reset",
                    $"In Sunshine's web page (https://{pc}:47990) open Troubleshooting and choose Unpair All Clients, then try again.");

            if (Has(status, "previous session is still running"))
                return new ConnectionProblem("The PC is busy",
                    "A game or stream is still running on the PC. Close it there, or restart Sunshine, then try again.");

            if (Has(status, "no \"") && Has(status, "app in sunshine"))
                return new ConnectionProblem("No Steam in Sunshine",
                    $"Sunshine has no app for Steam. Add one in its web page (https://{pc}:47990) under Applications.\n({status})");

            if (Has(status, "is sunshine running", "unexpected reply"))
                return new ConnectionProblem("Not Sunshine?", $"{pc} answered, but not like Sunshine does. Check the address and that Sunshine is running.");

            if (Has(status, "failed (error", "couldn't start the stream"))
                return new ConnectionProblem("The stream wouldn't start",
                    $"The PC answered, but the video connection failed. A firewall may be blocking Sunshine's ports, or the PC's screen can't be captured (on a Mac, allow Screen Recording for Sunshine).\n({status})");

            if (Has(status, "connection lost"))
                return new ConnectionProblem("Connection lost", $"The stream stopped. Check the Wi-Fi and that the PC is awake.\n({status})");

            return new ConnectionProblem("Couldn't connect", status);
        }

        static bool Has(string text, params string[] needles)
        {
            foreach (var needle in needles)
                if (text.IndexOf(needle, StringComparison.OrdinalIgnoreCase) >= 0)
                    return true;
            return false;
        }
    }
}

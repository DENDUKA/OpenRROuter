namespace OpenRussiaRouter.WebApp.Email;

/// <summary>
/// Pure builders for the outbound emails this app sends. Kept side-effect-free
/// (no I/O) so they can be unit-tested directly and reused by page handlers and
/// background services alike.
/// </summary>
public static class EmailTemplates
{
    /// <summary>
    /// Invite email: a plain-text message carrying the one-time registration link
    /// (<paramref name="inviteLink"/> is the full {scheme}://{host}/invite/{token} URL).
    /// </summary>
    public static EmailMessage InviteEmail(string recipient, string inviteLink)
    {
        const string subject = "You're invited to OpenRussiaRouter";
        var body =
            "Hello,\n\n" +
            "You've been invited to OpenRussiaRouter. Follow the link below to set a " +
            "password and activate your account:\n\n" +
            $"{inviteLink}\n\n" +
            "The link is single-use and expires in 7 days. If you didn't expect this " +
            "invitation, you can ignore this message.\n";
        return new EmailMessage(recipient, subject, body);
    }
}

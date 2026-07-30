using AssistantV2.App.Lifecycle;
using AssistantV2.App.Mail;
using AssistantV2.App.Registry;
using System;
using System.Linq;
using System.Threading.Tasks;

namespace AssistantV2.App.Smoke;

/// <summary>
/// In-process mail/registry smoke without a model. Run: AssistantV2.App --smoke
/// </summary>
public static class MailRegistrySmoke
{
    public static async Task<int> RunAsync()
    {
        var channel = new LifecycleEventChannel();
        var registry = new AgentRegistry(channel);
        var mail = new MailService(registry, channel);

        var root = registry.RegisterRoot("Secretary", "Manager", string.Empty, "Director");
        var child = registry.SpawnChild(root, "Coder", "Writes code", "Be brief", "Manager");

        if (child.State != AgentRunState.Idle)
        {
            Fail("spawn should leave child Idle");
        }

        mail.WriteFromUser("Please investigate flaky tests", "User request");
        if (!root.Inbox.HasMail())
        {
            Fail("root should have user mail");
        }

        var (okWrite, writeMsg) = mail.WriteMail(root, "Coder", "Flaky tests", "Find and fix the flake.");
        if (!okWrite)
        {
            Fail(writeMsg);
        }

        if (!child.Inbox.HasMail())
        {
            Fail("child should have mail from parent");
        }

        // Child was Idle → should have a wake signal
        if (!child.WakeChannel.Reader.TryRead(out _))
        {
            Fail("child should be woken by mail");
        }

        var childInbox = mail.ListInbox(child);
        if (childInbox[0].Status != MailStatus.New)
        {
            Fail("new mail should have status NEW");
        }

        var mailId = childInbox[0].Id;
        mail.ReadMail(child, mailId);
        var afterRead = mail.ListInbox(child);
        if (afterRead[0].Status != MailStatus.Read)
        {
            Fail("ReadMail should clear NEW status");
        }

        var (okReply, replyMsg) = mail.ReplyMail(child, mailId, "Fixed in PR 42.");
        if (!okReply)
        {
            Fail(replyMsg);
        }

        if (child.Inbox.HasMail())
        {
            Fail("reply should remove child inbox item");
        }

        if (!root.Inbox.HasMail())
        {
            Fail("root should receive child reply as inbox mail");
        }

        var userMailId = mail.ListInbox(root).First(item => item.From == "User").Id;
        var (okDelete, deleteMsg) = mail.DeleteMail(root, userMailId);
        if (!okDelete)
        {
            Fail(deleteMsg);
        }

        if (mail.ListInbox(root).Any(item => item.Id == userMailId))
        {
            Fail("deleted user mail should be gone");
        }

        var disposed = registry.DisposeSubtree(root, "Coder");
        if (disposed.Count != 1)
        {
            Fail("dispose should remove coder");
        }

        mail.PurgeMailFrom(root, ["Coder"]);
        var fromCoder = root.Inbox.List();
        foreach (var item in fromCoder)
        {
            if (item.From == "Coder")
            {
                Fail("purged coder mail should be gone");
            }
        }

        // Mid-turn notice path: Running agent gets notice instead of wake
        var researcher = registry.SpawnChild(root, "Researcher", "Researches", "Be brief", "Manager");
        if (!researcher.TryBeginRun())
        {
            Fail("researcher should begin run");
        }

        mail.WriteMail(root, "Researcher", "Topic", "Look into X");
        var notices = researcher.DrainPendingMailNotices();
        if (notices.Count != 1)
        {
            Fail("running agent should get mid-turn mail notice");
        }

        if (researcher.WakeChannel.Reader.TryRead(out _))
        {
            Fail("running agent should not be woken again");
        }

        researcher.EndRun();
        Console.WriteLine("MailRegistrySmoke: OK");
        return 0;
    }

    private static void Fail(string message) =>
        throw new InvalidOperationException(message);
}

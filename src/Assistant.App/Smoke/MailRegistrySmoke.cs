using Assistant.App.Lifecycle;
using Assistant.App.Mail;
using Assistant.App.Persistence;
using Assistant.App.Registry;
using System;
using System.Linq;
using System.Threading.Tasks;

namespace Assistant.App.Smoke;

/// <summary>
/// In-process mail/registry smoke without a model. Run: Assistant.App --smoke
/// </summary>
public static class MailRegistrySmoke
{
    public static async Task<int> RunAsync()
    {
        var channel = new LifecycleEventChannel();
        var store = new InMemoryAgentStateStore();
        var registry = new NodeRegistry(channel, store);
        var mail = new MailService(registry, channel, store);

        var user = registry.RegisterUser("Director");
        var root = registry.SpawnChild(user, "Secretary", "Manager", string.Empty, "Director");
        var child = registry.SpawnChild(root, "Coder", "Writes code", "Be brief", "Manager");

        if (child.State != NodeRunState.Idle)
        {
            Fail("spawn should leave child Idle");
        }

        var (okUser, userMsg) = mail.WriteMail(user, "Secretary", "User request", "Please investigate flaky tests");
        if (!okUser)
        {
            Fail(userMsg);
        }

        if (!root.Inbox.HasMail())
        {
            Fail("secretary should have user mail");
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

        if (child.Llm is null || !child.Llm.WakeChannel.Reader.TryRead(out _))
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
            Fail("secretary should receive child reply as inbox mail");
        }

        if (!user.Inbox.HasMail())
        {
            // Child replied to secretary, not user — user only has nothing new from this reply.
        }

        // Secretary replies to user mail
        var userMailId = mail.ListInbox(root).First(item => item.From == "User").Id;
        var (okReplyUser, replyUserMsg) = mail.ReplyMail(root, userMailId, "Looking into it.");
        if (!okReplyUser)
        {
            Fail(replyUserMsg);
        }

        if (!user.Inbox.HasMail())
        {
            Fail("user should receive secretary reply in inbox");
        }

        var replyToUserId = mail.ListInbox(user)[0].Id;
        var (okDelete, deleteMsg) = mail.DeleteMail(user, replyToUserId);
        if (!okDelete)
        {
            Fail(deleteMsg);
        }

        if (mail.ListInbox(user).Any(item => item.Id == replyToUserId))
        {
            Fail("deleted user mail should be gone");
        }

        var disposed = registry.DisposeSubtree(root, "Coder");
        if (disposed.Count != 1)
        {
            Fail("dispose should remove coder");
        }

        mail.PurgeMailFrom(root, ["Coder"]);
        foreach (var item in root.Inbox.List())
        {
            if (item.From == "Coder")
            {
                Fail("purged coder mail should be gone");
            }
        }

        var researcher = registry.SpawnChild(root, "Researcher", "Researches", "Be brief", "Manager");
        if (researcher.Llm is null || !researcher.Llm.TryBeginRun())
        {
            Fail("researcher should begin run");
        }

        mail.WriteMail(root, "Researcher", "Topic", "Look into X");
        var notices = researcher.Llm!.DrainPendingMailNotices();
        if (notices.Count != 1)
        {
            Fail("running agent should get mid-turn mail notice");
        }

        if (researcher.Llm.WakeChannel.Reader.TryRead(out _))
        {
            Fail("running agent should not be woken again");
        }

        researcher.Llm.EndRun();
        Console.WriteLine("MailRegistrySmoke: OK");
        await Task.CompletedTask;
        return 0;
    }

    private static void Fail(string message) =>
        throw new InvalidOperationException(message);
}

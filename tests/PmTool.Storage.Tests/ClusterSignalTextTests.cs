using PmTool.Abstractions;

namespace PmTool.Storage.Tests;

public sealed class ClusterSignalTextTests
{
    private static readonly Guid Origin = Guid.NewGuid();

    private static IReadOnlyList<Guid> People(int count) => Enumerable.Range(0, count).Select(_ => Guid.NewGuid()).ToList();

    [Test]
    public void Credentials_revoked_round_trips()
    {
        var account = Guid.NewGuid();
        var signal = new ClusterSignal(ClusterSignalKind.CredentialsRevoked) { AccountId = account, Origin = Origin };

        var parsed = ClusterSignalText.TryParse(ClusterSignalText.Serialize(signal), out var result);

        Assert.That(parsed, Is.True);
        Assert.That(result!.Kind, Is.EqualTo(ClusterSignalKind.CredentialsRevoked));
        Assert.That(result.AccountId, Is.EqualTo(account));
        Assert.That(result.Origin, Is.EqualTo(Origin));
    }

    [Test]
    public void Access_changed_round_trips_with_people_and_the_all_flag()
    {
        var people = People(3);
        var some = new ClusterSignal(ClusterSignalKind.AccessChanged) { PersonIds = people, Origin = Origin };
        var everyone = new ClusterSignal(ClusterSignalKind.AccessChanged) { AllPeople = true, Origin = Origin };

        Assert.That(ClusterSignalText.TryParse(ClusterSignalText.Serialize(some), out var a), Is.True);
        Assert.That(ClusterSignalText.TryParse(ClusterSignalText.Serialize(everyone), out var b), Is.True);

        Assert.That(a!.PersonIds, Is.EqualTo(people));
        Assert.That(a.AllPeople, Is.False);
        Assert.That(b!.AllPeople, Is.True);
        Assert.That(b.PersonIds, Is.Empty);
    }

    [Test]
    public void Access_changed_for_too_many_people_is_sent_as_everyone()
    {
        var signal = new ClusterSignal(ClusterSignalKind.AccessChanged) { PersonIds = People(151), Origin = Origin };

        var text = ClusterSignalText.Serialize(signal);

        Assert.That(ClusterSignalText.TryParse(text, out var parsed), Is.True);
        Assert.That(parsed!.AllPeople, Is.True);
        Assert.That(parsed.PersonIds, Is.Empty);
        Assert.That(text.Length, Is.LessThan(8000));
    }

    [Test]
    public void Inbox_changed_for_too_many_people_is_split_into_several_messages_that_fit()
    {
        var people = People(320);
        var signal = new ClusterSignal(ClusterSignalKind.InboxChanged) { PersonIds = people, Origin = Origin };

        var messages = ClusterSignalText.SerializeAll(signal);

        Assert.That(messages, Has.Count.EqualTo(3));
        Assert.That(messages.All(m => System.Text.Encoding.UTF8.GetByteCount(m) < 8000), Is.True);
        var parsed = messages.Select(m => { ClusterSignalText.TryParse(m, out var s); return s!; }).ToList();
        Assert.That(parsed.SelectMany(s => s.PersonIds), Is.EqualTo(people));
        Assert.That(() => ClusterSignalText.Serialize(signal), Throws.ArgumentException);
    }

    [Test]
    public void Serialize_all_keeps_a_small_signal_in_one_message()
    {
        var signal = new ClusterSignal(ClusterSignalKind.InboxChanged) { PersonIds = People(2), Origin = Origin };

        Assert.That(ClusterSignalText.SerializeAll(signal), Has.Count.EqualTo(1));
    }

    [Test]
    public void Uses_the_documented_first_version_shape()
    {
        var account = Guid.Parse("11111111-1111-1111-1111-111111111111");
        var signal = new ClusterSignal(ClusterSignalKind.CredentialsRevoked) { AccountId = account, Origin = account };

        Assert.That(ClusterSignalText.Serialize(signal), Is.EqualTo(
            "{\"v\":1,\"kind\":\"credentials_revoked\",\"origin\":\"11111111-1111-1111-1111-111111111111\",\"account\":\"11111111-1111-1111-1111-111111111111\"}"));
    }

    [TestCase("")]
    [TestCase("not json")]
    [TestCase("[]")]
    [TestCase("{\"v\":2,\"kind\":\"inbox_changed\",\"origin\":\"11111111-1111-1111-1111-111111111111\",\"people\":[]}")]
    [TestCase("{\"v\":1,\"kind\":\"something_new\",\"origin\":\"11111111-1111-1111-1111-111111111111\"}")]
    [TestCase("{\"v\":1,\"kind\":\"inbox_changed\",\"origin\":\"nope\",\"people\":[]}")]
    [TestCase("{\"v\":1,\"kind\":\"inbox_changed\",\"origin\":\"11111111-1111-1111-1111-111111111111\",\"people\":[\"nope\"]}")]
    [TestCase("{\"v\":1,\"kind\":\"credentials_revoked\",\"origin\":\"11111111-1111-1111-1111-111111111111\"}")]
    [TestCase("{\"v\":1,\"kind\":\"access_changed\",\"origin\":\"11111111-1111-1111-1111-111111111111\",\"people\":[]}")]
    public void Rejects_bad_payloads_without_throwing(string payload)
    {
        Assert.That(ClusterSignalText.TryParse(payload, out var signal), Is.False);
        Assert.That(signal, Is.Null);
    }

    [Test]
    public void Rejects_a_message_with_more_people_than_the_limit()
    {
        var people = string.Join(",", People(151).Select(p => $"\"{p}\""));
        var payload = $"{{\"v\":1,\"kind\":\"inbox_changed\",\"origin\":\"{Origin}\",\"people\":[{people}]}}";

        Assert.That(ClusterSignalText.TryParse(payload, out _), Is.False);
    }
}

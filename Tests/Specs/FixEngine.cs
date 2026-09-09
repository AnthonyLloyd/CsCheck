namespace Tests.Specs;

using System;

/// <summary>A hand written session engine in the shape production code actually takes: mutable flags and an ordered
/// chain of ifs, written from the FIX rules directly. It has one planted defect, to show what a conformance failure
/// looks like.
/// <para>This is the system under test, so it owns the vocabulary - the message kinds, the sequence relations, what a step
/// emits, and the connection status - and knows nothing about the specification that checks it. The dependency runs
/// engine to specification to tests and never back, because an implementation that referenced its own specification
/// could not be shipped without it.</para></summary>
public sealed class FixEngine
{
    /// <summary>HeartBtInt, in ticks.</summary>
    public const int Interval = 2;
    /// <summary>Counters saturate here. That is a concession to the specification rather than something a real engine
    /// would do, and it is what keeps the two comparable under <c>Conform</c>.</summary>
    public const int Cap = 3;

    /// <summary>Where one connection is in its life. The only name here that is not FIX vocabulary, deliberately:
    /// QuickFIX/n holds this in six independent booleans, and every FIX candidate means something else - SessionState
    /// is QuickFIX's whole session state, SessionStatus (tag 1409) an authentication reason code, TradingSessionStatus
    /// whether the market is open. Collapsing the booleans into one value is what makes a logout sent before a logon
    /// unrepresentable rather than merely unreachable. "Connection" because a FIX session outlives a connection,
    /// whereas this model starts at an accepted socket and ends at Disconnected.</summary>
    public enum ConnectionStatus { AwaitingLogon, LoggedOn, LogoutSent, Disconnected }

    /// <summary>Inbound message kinds. <c>Nothing</c> is a step with no inbound message: a clock tick, or one of
    /// our own sends. <c>LogonReset</c> is a Logon with ResetSeqNumFlag=Y. <c>GapFill</c> is SequenceReset-GapFill
    /// (GapFillFlag=Y); <c>SeqReset</c> is a bare SequenceReset-Reset (GapFillFlag=N) which ignores MsgSeqNum
    /// altogether, so its <see cref="Seq"/> argument is the relation of NewSeqNo instead.</summary>
    public enum In { Nothing, Logon, LogonReset, App, Heartbeat, TestRequest, ResendRequest, GapFill, SeqReset, Logout, Garbled }

    /// <summary>MsgSeqNum of an inbound message relative to the number we expect. For <c>SeqReset</c> this is the
    /// relation of NewSeqNo instead, since a bare SequenceReset ignores MsgSeqNum. <c>TooLowDup</c> is PossDupFlag=Y
    /// with a valid OrigSendingTime; <c>DupBadOrig</c> is PossDupFlag=Y with OrigSendingTime missing or later than
    /// SendingTime, which the session layer requires be rejected rather than ignored.</summary>
    public enum Seq { Expected, TooHigh, TooLow, TooLowDup, DupBadOrig }

    /// <summary>What the session emitted during a step.</summary>
    [Flags]
    public enum Out
    {
        None = 0, Logon = 1, Heartbeat = 2, TestRequest = 4, ResendRequest = 8, Resend = 16, Logout = 32,
        Reject = 64, App = 128,
    }

    /// <summary>An inbound message, abstracted to its kind and its sequence number relation.</summary>
    public readonly record struct Msg(In Kind, Seq Seq)
    {
        public override string ToString() => Seq == Seq.Expected ? Kind.ToString() : $"{Kind} {Seq}";
    }

    ConnectionStatus _status = ConnectionStatus.AwaitingLogon;
    int _expect = 1;
    int _next = 1;
    int _idle;
    int _quiet;
    int _queued;
    bool _gapOpen;
    bool _testSent;
    Out _sent;

    public ConnectionStatus Status => _status;
    /// <summary>The inbound sequence number expected next, QuickFIX's NextTargetMsgSeqNum.</summary>
    public int Expect => _expect;
    /// <summary>The outbound sequence number to put on the next message, QuickFIX's NextSenderMsgSeqNum. Every message
    /// sent takes one, and a gap on this side breaks the counterparty's recovery just as badly as one on theirs.</summary>
    public int Next => _next;
    public bool GapOpen => _gapOpen;
    public int Queued => _queued;
    public Out Sent => _sent;

    bool Up => _status is ConnectionStatus.LoggedOn or ConnectionStatus.LogoutSent;

    void Send(Out o)
    {
        _next = Math.Min(_next + 1, Cap);
        _sent |= o;
        _idle = 0;
    }

    void Terminate()
    {
        _status = ConnectionStatus.Disconnected;
        _gapOpen = false;
        _queued = 0;
        _testSent = false;
        _idle = 0;
        _quiet = 0;
    }

    void Consume() => _expect = Math.Min(_expect + 1, Cap);

    /// <summary>Only a message that passed validation and was dispatched proves the counterparty is alive.</summary>
    void Accept()
    {
        _quiet = 0;
        _testSent = false;
    }

    public void Inbound(Msg m)
    {
        _sent = Out.None;
        if (m.Kind == In.Garbled) return;

        if (m.Kind == In.Logout)
        {
            Accept();
            if (_status != ConnectionStatus.LogoutSent) Send(Out.Logout);
            Terminate();
            return;
        }

        if (m.Kind is In.Logon or In.LogonReset)
        {
            if (Up || (m.Kind == In.Logon && m.Seq == Seq.TooLow))
            {
                Send(Out.Logout);
                Terminate();
                return;
            }
            Accept();
            // ResetSeqNumFlag=Y resets both directions, and before the reply rather than after: QuickFIX calls
            // SessionState.Reset, which sets NextSenderMsgSeqNum and NextTargetMsgSeqNum to 1, and only then
            // generates the Logon response - so that response carries sequence number 1.
            if (m.Kind == In.LogonReset)
            {
                _expect = 1;
                _next = 1;
                _gapOpen = false;
                _queued = 0;
            }
            Send(Out.Logon);
            _status = ConnectionStatus.LoggedOn;
            if (m.Kind == In.LogonReset) Consume();
            else if (m.Seq == Seq.TooHigh)
            {
                Send(Out.ResendRequest);
                _gapOpen = true;
                _queued = Math.Min(_queued + 1, 2);
            }
            else Consume();
            return;
        }

        if (!Up)
        {
            Terminate();
            return;
        }

        if (m.Kind == In.SeqReset)
        {
            Accept();
            if (m.Seq == Seq.TooHigh)
            {
                _gapOpen = false;
                _queued = 0;
                Consume();
            }
            else Send(Out.Reject);
            return;
        }

        if (m.Seq == Seq.DupBadOrig)
        {
            Send(Out.Reject);
            return;
        }
        if (m.Seq == Seq.TooLowDup)
        {
            Consume(); // PLANTED DEFECT: an already processed duplicate must not advance the expected sequence number
            return;
        }
        if (m.Seq == Seq.TooLow)
        {
            Send(Out.Logout);
            Terminate();
            return;
        }
        if (m.Seq == Seq.TooHigh)
        {
            if (!_gapOpen) Send(Out.ResendRequest);
            _gapOpen = true;
            _queued = Math.Min(_queued + 1, 2);
            return;
        }
        Accept();
        if (m.Kind == In.TestRequest) Send(Out.Heartbeat);
        else if (m.Kind == In.ResendRequest) Send(Out.Resend);
        if (_gapOpen)
        {
            _gapOpen = false;
            _queued = 0;
        }
        Consume();
    }

    public void Tick()
    {
        _sent = Out.None;
        if (_testSent && _quiet >= Interval) { Terminate(); return; }
        if (_status is ConnectionStatus.AwaitingLogon or ConnectionStatus.LogoutSent)
        {
            if (_idle >= Interval) Terminate();
            else Age();
            return;
        }
        if (_quiet >= Interval)
        {
            Send(Out.TestRequest);
            _testSent = true;
        }
        else if (_idle >= Interval) Send(Out.Heartbeat);
        Age();
    }

    void Age()
    {
        _idle = Math.Min(_idle + 1, Cap);
        _quiet = Math.Min(_quiet + 1, Cap);
    }

    public void SendApp()
    {
        _sent = Out.None;
        Send(Out.App);
    }

    public void SendLogout()
    {
        _sent = Out.None;
        Send(Out.Logout);
        _status = ConnectionStatus.LogoutSent;
    }

    public void Drop()
    {
        _sent = Out.None;
        Terminate();
    }

    /// <summary>A new connection for the same session. Everything scoped to the connection is cleared, but the
    /// sequence numbers are not: they belong to the session and surviving the disconnect is what lets the
    /// counterparty ask for what it missed.</summary>
    public void Reconnect()
    {
        _status = ConnectionStatus.AwaitingLogon;
        _sent = Out.None;
        _gapOpen = false;
        _queued = 0;
        _testSent = false;
        _idle = 0;
        _quiet = 0;
    }
}

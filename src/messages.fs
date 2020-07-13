namespace   MyNamespace.Raft

open System.Net

type LogEntry=
    {term:int
     cmd:string }
    override  me.ToString() = sprintf "{t=%A,c=%A}" me.term me.cmd

type Message(src:string)=
    let mutable _src=src
    member me._kind="Message"
    member me.src
        with get()=_src
        and  set(value)= _src <- value


type RequestVoteA(src, term:int, candidateId:string, lastLogIndex:int, lastLogTerm:int )=
    inherit Message(src)
    member me._kind="RequestVoteA"
    member me.term = term
    member me.candidateId=candidateId
    member me.lastLogIndex=lastLogIndex
    member me.lastLogTerm=lastLogTerm


type RequestVoteB(src, req:RequestVoteA, term:int, voteGranded:bool )=
    inherit Message(src)
    member me._kind="RequestVoteB"
    member me.req=req
    member me.term = term
    member me.voteGranded=voteGranded

type AppendEntriesA(src, term:int, leaderId:string, prevLogIndex:int, prevLogTerm:int, entries:LogEntry array, leaderCommit:int )=
    inherit Message(src)
    member me._kind="AppendEntriesA"
    member me.term = term
    member me.leaderId = leaderId
    member me.prevLogIndex = prevLogIndex
    member me.prevLogTerm = prevLogTerm
    member me.entries = entries
    member me.leaderCommit = leaderCommit


type AppendEntriesB(src, req:AppendEntriesA, term:int, success:bool )=
    inherit Message(src)
    member me._kind="AppendEntriesB"
    member me.req = req
    member me.term = term
    member me.success=success






type Message with
    member me.hasterm() =
        match me with
        | :? RequestVoteA   as m ->  m.term 
        | :? RequestVoteB   as m ->  m.term
        | :? AppendEntriesA as m ->  m.term 
        | :? AppendEntriesB as m ->  m.term
        | _                      ->  -1
namespace   MyNamespace.Raft

open System.Net

type LogEntry=
    {term:int
     cmd:string 
     uid:System.Guid
     serial:int}
    override  me.ToString() = sprintf "{t=%A,c=%A}" me.term me.cmd

type Message(src:string)=
    let mutable _src=src
    let mutable _stamp=0
    let mutable _guid=System.Guid.NewGuid()

    member me._kind="Message"    
    member me.src
        with get()=_src
        and  set(value)= _src <- value
    member me.stamp
        with get()=_stamp
        and  set(value)= _stamp <- value
    member me.guid
        with get()=_guid
        and  set(value)= _guid <- value


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


//========================================
type ClientMsgA(src, uid:System.Guid, cmd:string)=
    inherit Message(src)
    member me._kind="ClientMsgA"
    member me.uid = uid
    member me.cmd = cmd

type ClientMsgB(src, req:ClientMsgA, rpl:string)=
    inherit Message(src)
    member me._kind="ClientMsgB"
    member me.req=req
    member me.rpl = rpl

//=============================================
type CtrlMsgSTOP(src)=
    inherit Message(src)
    member me._kind="CtrlMsgSTOP"

type CtrlMsgRESUME(src)=
    inherit Message(src)
    member me._kind="CtrlMsgRESUME"

type CtrlMsgRESTART(src)=
    inherit Message(src)
    member me._kind="CtrlMsgRESTART"







type Message with
    member me.hasterm() =
        match me with
        | :? RequestVoteA   as m ->  m.term 
        | :? RequestVoteB   as m ->  m.term
        | :? AppendEntriesA as m ->  m.term 
        | :? AppendEntriesB as m ->  m.term
        | _                      ->  -1
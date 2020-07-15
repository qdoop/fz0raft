namespace MyNamespace.Raft

open System.Net
open System.Net.Sockets
open System.Diagnostics
open System.Threading
open Newtonsoft.Json
open Microsoft.FSharp.Control

open MyNamespace.helpers
open MyNamespace.dblogger


type NodeState =
    | FOLLOWER  = 0
    | CANDIDATE = 1
    | LEADER    = 2


type Node(name:string, endpoint:string, config:string list) as me=
    let udp = new UdpClient( IPEndPoint.Parse(endpoint) )
    let peers = List.filter (fun x -> x<>endpoint ) config

    let _id = name.Substring(name.Length-1)

    let mutable _state= NodeState.FOLLOWER

    let mutable _timer=Stopwatch.StartNew()
    let maxtimeout=7000
    let mintimeout=5000
    let mutable _electtimeout= (new System.Random()).Next(mintimeout, maxtimeout)
    // let mutable _electtimeout=  if "12001"=name then
    //                                 7000
    //                             elif "12002"=name then
    //                                 6000
    //                             else
    //                                 5000

    let mutable _heartbeat = 2000
    let mutable _stepwait  = 1000
    let mutable _votes: Set<string> = Set.empty
    let mutable _heartbeats: Set<string> = Set.empty

    //for testing
    let mutable _killtimer=Stopwatch.StartNew()
    let mutable _killonce = false

    //Percistent
    let mutable _logBaseIndex=0  //SPECIAL FOR SNAPSHOTS
    let mutable _logBaseTerm=0   //SPECIAL FOR SNAPSHOTS
    let mutable _currTerm=0             
    let mutable _votedFor:string option = None
    let mutable _log:LogEntry list=[]     //[{term=1;cmd="nope";}]
    //Volatile
    let mutable _commIndex = _logBaseIndex // 0 //SPECIAL FOR SNAPSHOTS
    let mutable _lastApplied = _logBaseIndex // 0 //SPECIAL FOR SNAPSHOTS
    //Leader only
    let mutable _nextIndex:Map<string,int>=Map.empty
    let mutable _matchIndex:Map<string,int>=Map.empty

    //state machine logging
    let mutable _zlogPrevState=""
    let zlogState stagex =
        let obj ={|
                _kind       = "node" + _id
                stamp       = 0 //stamp is added latter to log only changed states
                state       = sprintf "%A" _state
                stage       = stagex           
                logBaseIndex= _logBaseIndex
                logBaseTerm = _logBaseTerm
                currTerm    = _currTerm            
                votedFor    = _currTerm
                log         = _log |> List.truncate 10 
                //Volatile
                commIndex   = _commIndex
                lastApplied = _lastApplied
                //Leader only
                nextIndex   = _nextIndex
                matchIndex  = _matchIndex 
            |}
        let json=JsonConvert.SerializeObject(obj)
        
        if _zlogPrevState <> json then
            _zlogPrevState <-json
            let obj={|obj with stamp=stamp()|}
            let json=JsonConvert.SerializeObject(obj)
            me.zlog json
            zdblog _id obj.stamp json
    let zlogStateMsg (msg:Message) =
        let json=JsonConvert.SerializeObject(msg)
        me.zlog json
        zdblog _id msg.stamp json
    let zlogStateCmd cmdx =
        let obj ={|
            _kind  = "Command"
            stamp  = stamp()
            cmd    = cmdx
            |}
        let json=JsonConvert.SerializeObject(obj)
        me.zlog json
        zdblog _id obj.stamp json

    let _statemachine=new MailboxProcessor<Message>( fun inbox ->
        //===================================================================
        let rec followerState (stage:int) =
            zlogState stage
            async{                
                _state <- NodeState.FOLLOWER
                me.zlog <| sprintf "%A%s" _state me.plog
                if 0=stage then
                    me.zlog <| sprintf "init %A" _state
                    me.TimerRestart()
                    ()

                if _electtimeout<me.elapsed then 
                    return! candidateState 0
                    ()

                if _commIndex > _lastApplied then
                    _lastApplied <- _lastApplied + 1
                    //apply _log[_lastApplied] to state machine
                    me.zlog <| sprintf "apply _log[%A]" _lastApplied
                    me.logTruncate( _lastApplied )
                    ()

                let! msg=inbox.TryReceive(_stepwait)
                if msg.IsSome then zlogStateMsg msg.Value
                if msg.IsSome then 
                    me.TimerRestart()
                match msg with
                |None     ->
                            return! followerState 1
                            ()
                |Some mx -> 
                            me.zlog <| sprintf "%A %A %A" mx  (mx.hasterm())  me.elapsed
                            match mx with
                            | :? RequestVoteA as m   ->
                                if m.term > _currTerm then
                                    _currTerm <- m.term
                                    _votedFor <- None //IMPORTANT
                                    //return! followerState 0 // IMPORTANT ALREADY FOLLOWER

                                if  m.term < _currTerm then
                                    let msg=new RequestVoteB("",m,_currTerm, false)
                                    me.SendMessage(m.src, msg)
                                    return! followerState 1

                                if _votedFor.IsSome && _votedFor.Value = m.candidateId then
                                    let msg=new RequestVoteB("",m,_currTerm, true)
                                    me.SendMessage(m.src, msg)
                                    return! followerState 1                                

                                if _votedFor.IsSome && _votedFor.Value <> m.candidateId then
                                    let msg=new RequestVoteB("",m,_currTerm, false)
                                    me.SendMessage(m.src, msg)
                                    return! followerState 1 

                                if m.lastLogTerm = _logBaseTerm then  //MEANS log empty
                                    _votedFor<-Some m.candidateId
                                    let msg=new RequestVoteB("",m,_currTerm, true)
                                    me.SendMessage(m.src, msg)
                                    return! followerState 1

                                if m.lastLogTerm > _log.Head.term then
                                    _votedFor<-Some m.candidateId
                                    let msg=new RequestVoteB("",m,_currTerm, true)
                                    me.SendMessage(m.src, msg)
                                    return! followerState 1

                                if m.lastLogTerm = _log.Head.term  && m.lastLogIndex >= me.logIndex then
                                    _votedFor<-Some m.candidateId
                                    let msg=new RequestVoteB("",m,_currTerm, true)
                                    me.SendMessage(m.src, msg)
                                    return! followerState 1                                
                                    
                                let msg=new RequestVoteB("",m,_currTerm, false)
                                me.SendMessage(m.src, msg)
                                ()

                            | :? RequestVoteB as m   ->
                                if m.term > _currTerm then
                                    _currTerm <- m.term
                                    _votedFor <- None //IMPORTANT
                                    return! followerState 0   
                                ()

                            | :? AppendEntriesA as m ->   
                                if m.term > _currTerm then
                                    _currTerm <- m.term
                                    _votedFor <- None //IMPORTANT
                                    return! followerState 1 

                                if m.term < _currTerm then
                                    let msg=new AppendEntriesB ("",m,_currTerm,false)
                                    me.SendMessage(m.src, msg)
                                    return! followerState 1  
                                
                                //now m.term == _currentTerm

                                assert(m.prevLogIndex >=_logBaseIndex ) ///VERY IMPORTANT SNAPSHOOT
                                if m.prevLogIndex=_logBaseIndex then  //IMPORTANT ALWAYS SUCCEDES
                                    _log <- [] //SO JUST CLEAR THE LOG
                                    for x in m.entries do _log <- x :: _log
                                    if m.leaderCommit > _commIndex then _commIndex <- min m.leaderCommit me.logIndex
                                    let msg=new AppendEntriesB ("",m, _currTerm,true)
                                    me.SendMessage(m.src, msg)
                                    return! followerState 1

                                if m.prevLogIndex > me.logIndex then                                   
                                    let msg=new AppendEntriesB ("",m,_currTerm,false)
                                    me.SendMessage(m.src, msg)
                                    return! followerState 1

                                // at this point m.prevLogIndex SURE NONZERO so list shoule be no empty
                                if m.prevLogIndex=me.logIndex &&  m.prevLogTerm=_log.Head.term then
                                    for x in m.entries do _log <- x :: _log
                                    if m.leaderCommit > _commIndex then _commIndex <- min m.leaderCommit me.logIndex
                                    let msg=new AppendEntriesB ("",m,_currTerm,true)
                                    me.SendMessage(m.src, msg)
                                    return! followerState 1 

                                if m.prevLogIndex=me.logIndex && m.prevLogTerm<>_log.Head.term then
                                    let msg=new AppendEntriesB ("",m,_currTerm,false)
                                    me.SendMessage(m.src, msg)
                                    return! followerState 1                                 

                                if m.prevLogIndex<me.logIndex &&  m.prevLogTerm=_log.[m.prevLogIndex - _logBaseIndex - 1].term then
                                    _log <- List.skip (me.logIndex - m.prevLogIndex ) _log
                                    for x in m.entries do _log <- x :: _log
                                    if m.leaderCommit > _commIndex then _commIndex <- min m.leaderCommit me.logIndex
                                    let msg=new AppendEntriesB ("",m,_currTerm,true)
                                    me.SendMessage(m.src, msg)
                                    return! followerState 1 

                                if m.prevLogIndex<me.logIndex && m.prevLogTerm<>_log.[m.prevLogIndex - _logBaseIndex - 1].term then
                                    let msg=new AppendEntriesB ("",m,_currTerm,false)
                                    me.SendMessage(m.src, msg)
                                    return! followerState 1 

                                //never reach here
                                assert(false)
                                let msg=new AppendEntriesB ("",m,_currTerm,false)
                                me.SendMessage(m.src, msg)
                                ()

                            | :? AppendEntriesB as m  ->   
                                if m.term > _currTerm then
                                    _currTerm <- m.term
                                    _votedFor <- None //IMPORTANT
                                    return! followerState 0   
                                () 
                            |  _                      ->
                                assert(false)    
                                ()

                return! followerState 1
            }
        //===================================================================
        and candidateState (stage:int) =
            zlogState stage
            async{
                _state <- NodeState.CANDIDATE
                me.zlog <| sprintf "%A" _state
                if 0=stage then
                    me.zlog <| sprintf "init %A" _state

                    _electtimeout <- (new System.Random()).Next(mintimeout, maxtimeout)

                    me.TimerRestart()
                    _currTerm <- _currTerm + 1
                    _votes <- Set.empty 
                    _votes <- _votes.Add(endpoint) //I vote for myself
                    peers |> List.iter ( fun x ->                        
                        let msg=new RequestVoteA("",_currTerm, name, me.logIndex, (if _log.IsEmpty then _logBaseTerm else _log.Head.term))                        
                        me.SendMessage(x, msg)
                        )
                    ()

                if _electtimeout<me.elapsed then
                    return! candidateState 0
                    ()

                if _commIndex > _lastApplied then
                    _lastApplied <- _lastApplied + 1
                    //apply _log[_lastApplied] to state machine
                    me.zlog <| sprintf "apply _log[%A]" _lastApplied
                    me.logTruncate( _lastApplied )
                    ()

                let! msg=inbox.TryReceive(_stepwait)
                if msg.IsSome then zlogStateMsg msg.Value
                match msg with
                |None    ->
                            return! candidateState 1
                            () 
                |Some mx -> 
                            me.zlog <| sprintf "%A %A" mx me.elapsed

                            if mx.hasterm() > _currTerm then 
                                    _currTerm <- mx.hasterm()
                                    _votedFor <- None //IMPORTANT
                                    return! followerState 0

                            match mx with
                            | :? RequestVoteA as m   ->
                                ()
                            | :? RequestVoteB as m   ->   
                                me.zlog <| sprintf "voteGranded %A " m.voteGranded
                                if m.voteGranded then _votes <- _votes.Add(m.src)
                                me.zlog <| sprintf "_vote %A " _votes
                                if 2 * _votes.Count > config.Length then
                                    return! leaderState 0
                                ()
                            | :? AppendEntriesA as m ->   
                                return! followerState 0
                                ()
                            | :? AppendEntriesB as m  ->   
                                ()
                            |  _                      ->
                                assert(false)    
                                ()

                return! candidateState 1
            }
        //====================================================================
        and leaderState (stage:int) =
            zlogState stage
            async{
                _state <- NodeState.LEADER
                me.zlog <| sprintf "%A  %s" _state me.plog
                if 0=stage then
                    me.zlog <| sprintf "init %A" _state
                    me.TimerRestart()
                    let r=config |> List.iter ( fun x ->
                        _nextIndex  <- _nextIndex.Add( x, me.logIndex + 1)
                        _matchIndex <- _matchIndex.Add(x, 0)
                    )
                    peers |> List.iter ( fun x ->
                        let pterm = if _log.IsEmpty then _logBaseTerm else _log.Head.term                        
                        let msg=new AppendEntriesA("",_currTerm, name, me.logIndex, pterm , [||], _commIndex)                        
                        me.SendMessage(x, msg)
                        )
                    ()

                let heartbeatFun x =
                    let nextIndex = _nextIndex.TryFind x
                    let nextIndex = nextIndex.Value
                    if me.logIndex >= nextIndex then

                        let t0=List.truncate (me.logIndex - nextIndex) _log |> List.rev 
                        let t1= t0|> List.truncate 2 |> List.toArray
                        //me.log <| sprintf "truncated %A %A %A" me.logIndex  nextIndex t
                        let pos=me.logIndex - _logBaseIndex - t0.Length - 1 
                        // if pos > -1 then
                        //     me.log <| sprintf "\n\n_log.[pos].cmd %A " _log.[pos].cmd

                        let pterm = if me.logIndex - _logBaseIndex=t0.Length then _logBaseTerm else _log.[pos].term
                        let msg=new AppendEntriesA("",_currTerm, name, me.logIndex-t0.Length, pterm, t1, _commIndex)                        
                        me.SendMessage(x, msg)
                    else
                        let pterm = if _log.IsEmpty then _logBaseTerm else _log.Head.term
                        let msg=new AppendEntriesA("", _currTerm, name, me.logIndex, pterm, [||], _commIndex)                        
                        me.SendMessage(x, msg)                        
                    


                
                for x in _heartbeats do
                    heartbeatFun x
                    ()
                _heartbeats <- Set.empty


                if _heartbeat<me.elapsed then 
                    me.TimerRestart()
                    peers |> List.iter heartbeatFun                    
                    ()

                if triggerClientMsg.IsSome then
                    let ccmd = triggerClientMsg.Value
                    triggerClientMsg <- None                 
                    me.zlog <|  sprintf "\n\n\n===============\nClient msg : %A" ccmd

                    zlogStateCmd ccmd

                    _log <- {term=_currTerm; cmd=ccmd } :: _log                        
                    
                    peers |> List.iter heartbeatFun 
                    ()


                if _killonce && "12003"=name && 10000 < int _killtimer.Elapsed.TotalMilliseconds then
                    _killonce <- false
                    Async.Sleep(30000) |> Async.RunSynchronously
                    // _killtimer <- Stopwatch.StartNew()

                if 0 = System.Random().Next(1, 500) then
                    me.zlog <| sprintf "@@@@@@@@@@@@@@@@@@@@@@@@@@@@@@@@@@@@@@@@ KILLED!!!"
                    Async.Sleep(30000) |> Async.RunSynchronously
                    me.zlog <| sprintf "**************************************** ALIVE!!!"


                if _commIndex > _lastApplied then
                    _lastApplied <- _lastApplied + 1
                    //apply _log[_lastApplied] to state machine
                    me.zlog <| sprintf "------------------------------apply _log[%A]" _lastApplied
                    me.logTruncate( _lastApplied )
                    ()

                let! msg=inbox.TryReceive(_stepwait)
                if msg.IsSome then zlogStateMsg msg.Value
                match msg with
                |None     ->
                            return! leaderState 1
                            ()
                |Some mx -> 
                            me.zlog <| sprintf "%A %A" mx me.elapsed

                            if mx.hasterm() > _currTerm then 
                                    _currTerm <- mx.hasterm()
                                    _votedFor <- None //IMPORTANT
                                    return! followerState 0

                            match mx with 
                            | :? RequestVoteA as m   ->                          
                                ()
                            | :? RequestVoteB as m   ->   
                                ()
                            | :? AppendEntriesA as m ->   
                                ()
                            | :? AppendEntriesB as m  ->
                                if not m.success && m.term=_currTerm  then
                                    let nextIndex =_nextIndex.TryFind m.src
                                    let nextIndex = nextIndex.Value - 1 //always succeeds
                                    let nextIndex = max nextIndex 0
                                    _nextIndex <- _nextIndex.Add(m.src, nextIndex) 
                                    _heartbeats <- _heartbeats.Add(m.src)
                                    return! leaderState 1

                                if m.success then
                                    _nextIndex  <- _nextIndex.Add(m.src, m.req.prevLogIndex + m.req.entries.Length)
                                    _nextIndex  <- _nextIndex.Add(endpoint, me.logIndex)
                                    _matchIndex <- _matchIndex.Add(m.src, m.req.prevLogIndex + m.req.entries.Length)
                                    _matchIndex <- _matchIndex.Add(endpoint, me.logIndex)
                                    
                                    let r=config |> List.map ( fun x -> 
                                        let matchIndex = _matchIndex.TryFind x //always succeeds
                                        let matchIndex = matchIndex.Value
                                        matchIndex
                                        )
                                    while true do
                                        let N = _commIndex + 1
                                        if N > me.logIndex || _log.[N-1-_logBaseIndex].term <> _currTerm then
                                            return! leaderState 1
                                        let r0 = r |> List.map ( fun x -> if N <= x then 1 else 0 ) |> List.sum 
                                        if 2 * r0 > config.Length then
                                            _commIndex <- N
                                            ()
                                        else
                                            return! leaderState 1
                                            ()
                                        ()
                                    return! leaderState 1
                                () 
                            |  _                      ->
                                assert(false)    
                                ()

                return! leaderState 1
            }
        
        me.TimerRestart()
        followerState 0
        )











    do
        try
            
            printfn "id %A udp %A peers %A" name endpoint peers
            printfn "election timeout %A" _electtimeout
            udp.DontFragment <- true
            udp.Ttl <- 255s; 
        with
            | ex -> printf "exception: %A\r\n" ex 




       
    member me.state 
        with get () =       _state
        and set (value) = 
                            _state <- value
                            printfn "id %A state=%A" name value
                            //_timeout <- (new System.Random()).Next(mintimeout, maxtimeout)

    member me.logIndex 
        with get () = _log.Length + _logBaseIndex

    member me.logTruncate(applied)=
        let keeplen=50
        if 2*keeplen < (applied - _logBaseIndex) then
            me.zlog <| sprintf "~~~~~~~~~ Truncating..."
            let keep= List.truncate (_log.Length - keeplen) _log
            _logBaseIndex <- _logBaseIndex + _log.Length - keep.Length
            _logBaseTerm  <- _log.[keep.Length].term
            _log <- keep



    member me.zlog s=
        Monitor.Enter lockobj
        printfn "id:%s | %s" _id s
        Monitor.Exit lockobj


    member me.plog
        with get() =
            let mutable str="["
            for x in (List.truncate 5 _log) do
                str <- str + sprintf "{%A,%A}" x.term x.cmd
            str <- str + sprintf " ... ] %A"  _log.Length
            str

    member me.SendMessage(edp:string, msg:Message)=
        let edp=IPEndPoint.Parse(edp)
        msg.src <- "node" + _id
        msg.stamp <- stamp()
        zlogStateMsg msg
        let json=JsonConvert.SerializeObject(msg)
        let payload=System.Text.Encoding.UTF8.GetBytes(json)
        // Thread.Sleep( (new System.Random()).Next(100))  
        let txlen=udp.Send(payload, payload.Length, edp)
        ()

    member me.TimerRestart()=
        _timer<-Stopwatch.StartNew()    
    member me.elapsed
        with get() = int _timer.Elapsed.TotalMilliseconds

    member me.udpthread=new Thread( fun () ->
        while true do
            let r = udp.ReceiveAsync() |> Async.AwaitTask |> Async.RunSynchronously
            let msg=ParseMessage r.RemoteEndPoint r.Buffer

            //dedublicate and then post
            _statemachine.Post(msg)
            ()
        )    
    member me.clientthread=new Thread( fun () ->
        let mutable i=0
        while true do
            Thread.Sleep(4000)
            triggerClientMsg <- Some (sprintf "%scmd%A" _id i)
            i <- i + 1

        // Thread.Sleep(8000)
        // triggerClientMsg <- Some "cmd0"
        // Thread.Sleep(8000)
        // triggerClientMsg <- Some "cmd1"        

        )     
    member public me.Start()=  
        _statemachine.Start()
        me.udpthread.Start()
        me.clientthread.Start()
        


    
    
 
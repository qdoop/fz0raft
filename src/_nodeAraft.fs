namespace MyNamespace.Raft

open System.Net
open System.Net.Sockets
open System.Diagnostics
open System.Threading
open Newtonsoft.Json
open Microsoft.FSharp.Control

open MyNamespace.globals
open MyNamespace.helpers
open MyNamespace.dblogger
open MyNamespace.filelogger
open System.IO


type NodeState =
    | STOPPED   = 0  //Special case to allow node control
    | FOLLOWER  = 1
    | CANDIDATE = 2
    | LEADER    = 3

type PersistentState()=
    member val logBaseIndex=0  with get,set//SPECIAL FOR SNAPSHOTS
    member val logBaseTerm=0   with get,set//SPECIAL FOR SNAPSHOTS
    member val currTerm=0      with get,set       
    member val votedFor:string option = None with get,set
    member val log:LogEntry list=[]  with get,set   //[{term=1;cmd="nope";}]


type Node(name:string, endpoint:string, config:string list) as me=
    let udp = new UdpClient( IPEndPoint.Parse(endpoint) )
    let peers = List.filter (fun x -> x<>endpoint ) config

    let _id = name.Substring(name.Length-1)
    let mutable _state= NodeState.FOLLOWER

    let mutable _timebase=System.DateTime.UtcNow//Stopwatch.StartNew()
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
    let mutable _stepwait  = 500
    let mutable _votes: Set<string> = Set.empty
    let mutable _heartbeats: Set<string> = Set.empty
    let mutable _stoppedStatePrev=fun (i:int) -> async{return()}
    let mutable _clientcmds:(ClientMsgA*int) list = []

    //for testing
    let mutable _killtimer=Stopwatch.StartNew()
    let mutable _killonce = false


    let _externstate=new ExternState()
    //================================== RAFT STATE
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
    //===========================================

    //state machine logging
    let mutable _zlogPrevState=""
    let zlogState stagex =
        Monitor.Enter lockobj
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
                loglen      = _log.Length 
                exscmdcnt   = me.externstate.CmdCount
                clientcmds  = _clientcmds.Length
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
            // me.zlog json
            zdblog _id obj.stamp json
        Monitor.Exit lockobj

    let zlogStateMsg (msg:Message) =
        Monitor.Enter lockobj
        let json=JsonConvert.SerializeObject(msg)
        // me.zlog json
        zdblog _id msg.stamp json
        Monitor.Exit lockobj


    let _raftFSM=new MailboxProcessor<Message>( fun inbox ->
        //===================================================================
        let rec followerState (stage:int) =            
            async{
                _state <- NodeState.FOLLOWER    
                zlogState stage

                me.zlog <| sprintf "%A%A%s" _state stage me.plog
                if 0=stage then
                    me.zlog <| sprintf "init %A" _state
                    me.TimerRestart()
                    ()

                if _electtimeout<me.elapsed then 
                    return! candidateState 0
                    ()

                while _commIndex > _lastApplied do
                    _lastApplied <- _lastApplied + 1
                    //apply _log[_lastApplied] to state machine
                    me.zlog <| sprintf "apply _log[%A]" _lastApplied

                    let b=me.externstate.ApplyCommand( _lastApplied, _log.[_log.Length - (_lastApplied - _logBaseIndex)])
                    if not b then 
                        assert(b)

                    me.logTruncate( _lastApplied )
                    ()

                let! msg=inbox.TryReceive(_stepwait)
                if msg.IsSome then zlogStateMsg msg.Value
                match msg with
                |None     ->
                            return! followerState 1
                            ()
                |Some mx -> 
                            me.zlog <| sprintf "%A %A %A" mx  (mx.hasterm())  me.elapsed
                            match mx with
                            | :? RequestVoteA as m   ->
                                me.TimerRestart()
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
                                me.TimerRestart()   
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
                            | :? ClientMsgA as m      ->
                                ()
                            | :? CtrlMsgSTOP as m      ->
                                _stoppedStatePrev <- followerState
                                return! stoppedState 0
                                ()                                 
                            |  _                      ->
                                return! stoppedState 0   
                                ()

                return! followerState 1
            }
        //===================================================================
        and candidateState (stage:int) =            
            async{
                _state <- NodeState.CANDIDATE
                zlogState stage


                me.zlog <| sprintf "%A%A" _state stage
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

                while _commIndex > _lastApplied do
                    _lastApplied <- _lastApplied + 1
                    //apply _log[_lastApplied] to state machine
                    me.zlog <| sprintf "apply _log[%A]" _lastApplied
                
                    let b=me.externstate.ApplyCommand( _lastApplied, _log.[_log.Length - (_lastApplied - _logBaseIndex)])
                    if not b then 
                        assert(b)

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
                            | :? ClientMsgA as m      ->
                                ()
                            | :? CtrlMsgSTOP as m      ->
                                _stoppedStatePrev <- candidateState
                                return! stoppedState 0
                                ()  
                            |  _                      ->
                                return! stoppedState 0    
                                ()

                return! candidateState 1
            }
        //====================================================================
        and leaderState (stage:int) =
            async{
                _state <- NodeState.LEADER
                zlogState stage


                me.zlog <| sprintf "%A%A  %s" _state stage me.plog
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
                        let t1= t0|> List.truncate 10 |> List.toArray
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

                // Simulate reseived client commands
                // if _clientcmdtimeout < stamp() && 1 = System.Random().Next(1, 10) then
                //     _clientcmdtimeout <- stamp() + 500
                //     let ccmd = sprintf "%scmd%A" _id _clientcmdid
                //     _clientcmdid <- _clientcmdid + 1
                //     me.zlog <|  sprintf "\n\n\n===============\nClient msg : %A" ccmd
                //     zlogStateCmd ccmd
                //     _log <- {term=_currTerm; cmd=ccmd } :: _log
                //     peers |> List.iter heartbeatFun 
                //     ()

                while _commIndex > _lastApplied do
                    _lastApplied <- _lastApplied + 1
                    //apply _log[_lastApplied] to state machine
                    me.zlog <| sprintf "------------------------------apply _log[%A]" _lastApplied

                    let b=me.externstate.ApplyCommand( _lastApplied, _log.[_log.Length - (_lastApplied - _logBaseIndex)])
                    if not b then                        
                        assert(b)

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
                            | :? ClientMsgA as m      ->
                                me.zlog <|  sprintf "\n\n\n===============\nClient msg : %A" m.cmd
                                _log <- {term=_currTerm; cmd=m.cmd; uid=m.uid;} :: _log
                                peers |> List.iter heartbeatFun

                                //m.uid <- _log.Head.uid
                                Monitor.Enter lockobj
                                _clientcmds <- (m, stamp()) :: _clientcmds
                                Monitor.Exit  lockobj

                                return! leaderState 1 
                                ()
                            | :? CtrlMsgSTOP as m      ->
                                _stoppedStatePrev <- leaderState
                                return! stoppedState 0
                                () 
                            |  _                      ->                   
                                return! stoppedState 0    
                                ()

                return! leaderState 1
            }
        and stoppedState (stage:int) =   //Special state to allow simulation control         
            async{
                _state <- NodeState.STOPPED
                zlogState stage

                let! msg=inbox.TryReceive(_stepwait)
                // if msg.IsSome then zlogStateMsg msg.Value
                match msg with
                |None     ->
                            return! stoppedState 1
                            ()
                |Some mx -> 
                            me.zlog <| sprintf "%A %A" mx me.elapsed
                            match mx with 
                            | :? CtrlMsgRESUME as m   ->
                                return! _stoppedStatePrev 1
                                ()
                            | :? CtrlMsgRESTART as m   ->
                                
                                me.loadPersistentState()

                                //reinit volatile state
                                _commIndex   <- _logBaseIndex // 0 //SPECIAL FOR SNAPSHOTS
                                _lastApplied <- _logBaseIndex // 0 //SPECIAL FOR SNAPSHOTS    
                                _nextIndex   <- Map.empty
                                _matchIndex  <- Map.empty
                                return! followerState 0
                                ()      
                            |  _                     ->    
                                ()
                return! stoppedState 1
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
        ()



    member me.externstate:ExternState=  _externstate 
    member me.state 
        with get () =       _state
        and set (value) = 
                            _state <- value
                            printfn "id %A state=%A" name value
                            //_timeout <- (new System.Random()).Next(mintimeout, maxtimeout)

    member me.logIndex 
        with get () = _log.Length + _logBaseIndex

    member me.logTruncate(applied)=
        let keeplen=1000
        if 2*keeplen < (applied - _logBaseIndex) then
            me.zlog <| sprintf "~~~~~~~~~ Truncating..."
            let keep= List.truncate (_log.Length - keeplen) _log
            _logBaseIndex <- _logBaseIndex + _log.Length - keep.Length
            _logBaseTerm  <- _log.[keep.Length].term
            _log <- keep


    member me.savePersistentState()=
        let obj=new PersistentState()
        obj.logBaseIndex <- _logBaseIndex
        obj.logBaseTerm  <- _logBaseTerm
        obj.currTerm     <- _currTerm
        obj.votedFor     <- _votedFor
        obj.log          <- _log

        let json = JsonConvert.SerializeObject(obj)
        let sw = new StreamWriter("pstate" + _id + ".json", false)
        sw.WriteLine(json)
        sw.Dispose()
        ()

    member me.loadPersistentState()=
        let sr = new StreamReader("pstate" + _id + ".json", true)
        let line=sr.ReadLine()
        sr.Dispose()
        let o=JsonConvert.DeserializeObject<PersistentState>(line)

        _logBaseIndex <- o.logBaseIndex
        _logBaseTerm  <- o.logBaseTerm
        _currTerm     <- o.currTerm
        _votedFor     <- o.votedFor
        _log          <- o.log
        ()


    member me.stop()=    
        _raftFSM.Post( new CtrlMsgSTOP("web"))
    member me.resume()=  
        _raftFSM.Post( new CtrlMsgRESUME("web"))
    member me.restart()=  
        _raftFSM.Post( new CtrlMsgRESTART("web"))
    member me.clientcmd(msg:Message)=  
        _raftFSM.Post( msg)

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
        
        me.savePersistentState()

        if "127.0.0.1:12008" <> string edp then  
            let txlen=udp.Send(payload, payload.Length, edp)
            ()
        ()

    member me.TimerRestart()=
        _timebase<- System.DateTime.UtcNow//Stopwatch.StartNew()    
    member me.elapsed
        with get() = int (System.DateTime.UtcNow - _timebase).TotalMilliseconds

    member me.udpthread=new Thread( fun () ->
        while true do
            let r = udp.ReceiveAsync() |> Async.AwaitTask |> Async.RunSynchronously
            let msg=ParseMessage r.RemoteEndPoint r.Buffer

            //dedublicate and then post
            _raftFSM.Post(msg)
            ()
        )    
    member me.clientthread=new Thread( fun () ->
        let mutable i=0
        while true do
            Thread.Sleep(0)
            Monitor.Enter lockobj
            match List.tryFindIndex (fun x -> ((snd x)+255_000)<stamp()) _clientcmds with
            | Some pos -> _clientcmds <- List.truncate pos _clientcmds;() 
            | None -> ()

            let rec HandleCommand (list:(ClientMsgA*int)list)=
                match list with
                | head :: tail ->
                                let m=(fst head)
                                let v=me.externstate.CheckCommand( m.uid )
                                match v with
                                | Some le -> 
                                                let msg= new ClientMsgB("",m,"ok")
                                                me.SendMessage(m.src, msg)
                                                _clientcmds <- _clientcmds |> List.filter ( fun x -> 
                                                    let m0= fst x
                                                    m0.uid<>m.uid
                                                    )
                                                HandleCommand _clientcmds
                                | None    ->
                                                HandleCommand tail
                                
                                ()
                | []            ->
                                ()

            HandleCommand _clientcmds
            Monitor.Exit lockobj
        )
    member public me.Start()= 
        _raftFSM.Error.Add(fun exn ->
            match exn with
            | _ -> printfn "AGENT EXCEPTION. id=%A | %A" _id  exn
                   exit(1) ) 
        _raftFSM.Start()
        me.udpthread.Start()
        me.clientthread.Start()
        


    
    
 
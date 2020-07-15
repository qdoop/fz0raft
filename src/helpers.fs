module MyNamespace.helpers

open System.Net
open System.Net.Sockets
open System.Diagnostics
open Newtonsoft.Json
open System.Threading
open System.Threading
open System.Threading.Tasks

open MyNamespace.Raft

let globaltimer=Stopwatch.StartNew()
let stamp() = int globaltimer.Elapsed.TotalMilliseconds

let lockobj=new obj()

let mutable triggerClientMsg:string option=None

type Microsoft.FSharp.Control.Async with
    static member AwaitTask (id:string, t:Task<'T>, timeout:int) =
        async {
            let stopWatch = Stopwatch.StartNew()
            use cts = new CancellationTokenSource()
            use timer = Task.Delay (timeout, cts.Token)
            let! completed = Async.AwaitTask <| Task.WhenAny(t, timer)
            printfn "id %A timeout: %A" id stopWatch.Elapsed.TotalMilliseconds
            if completed = (t :> Task) then
                cts.Cancel ()
                let! result = Async.AwaitTask t                
                return Some result
            else 
                return None
        }


let max x y = if x>y then x else y
let min x y = if x>y then y else x

let ParseMessage (edp:IPEndPoint) (payload:byte array) =
        let json=System.Text.Encoding.UTF8.GetString(payload)
        let prefix="{\"_kind\":\""
        let mutable msg = new Message("0.0.0.0:0")
        if 0=json.IndexOf(prefix + "Message") then
            msg <- JsonConvert.DeserializeObject<Message>(json)
        elif 0=json.IndexOf(prefix + "RequestVoteA") then
            msg <- JsonConvert.DeserializeObject<RequestVoteA>(json)
        elif 0=json.IndexOf(prefix + "RequestVoteB") then
            msg <- JsonConvert.DeserializeObject<RequestVoteB>(json)
        elif 0=json.IndexOf(prefix + "AppendEntriesA") then
            msg <- JsonConvert.DeserializeObject<AppendEntriesA>(json)
        elif 0=json.IndexOf(prefix + "AppendEntriesB") then
            msg <- JsonConvert.DeserializeObject<AppendEntriesB>(json)


        assert(json=JsonConvert.SerializeObject(msg))        
        msg.src <- edp.ToString()
        msg.stamp <- stamp()        
        msg



let debugMsg (id:string) (msg:Message) =
    printfn "id=%A <- msg=%A" id (JsonConvert.SerializeObject(msg))




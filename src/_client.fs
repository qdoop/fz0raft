namespace MyNamespace.Raft

open System.Net
open System.Net.Sockets
open System.Diagnostics
open System.Threading
open Newtonsoft.Json
open Microsoft.FSharp.Control

open MyNamespace.Raft
open MyNamespace.globals
open MyNamespace.helpers
open MyNamespace.dblogger
open MyNamespace.filelogger

type ClientNode(endpoint:string, config:string list) as me =
    let udp = new UdpClient( IPEndPoint.Parse(endpoint) )

    let _clientthread=new Thread( fun () ->
        let mutable i = -1
        while true do
            i <- i + 1    
            Thread.Sleep(4000)
            config |> List.iter ( fun x ->
                let cmd = sprintf "cmd%A" i                    
                let msg=new ClientMsgA("client", System.Guid.NewGuid(), cmd)                       
                me.SendMessage(x, msg)
            )

        )

    do
        _clientthread.Start()

    member me.zlog s=
        Monitor.Enter lockobj
        printfn "id:%s | %s" " " s
        Monitor.Exit lockobj

    member me.SendMessage(edp:string, msg:Message)=
        let edp=IPEndPoint.Parse(edp)
        let json=JsonConvert.SerializeObject(msg)
        let payload=System.Text.Encoding.UTF8.GetBytes(json)
        // Thread.Sleep( (new System.Random()).Next(100))  
        let txlen=udp.Send(payload, payload.Length, edp)
        ()

module MyNamespace.dbviewer

open System.Text
open Newtonsoft.Json
open Suave
open Suave.CORS
open Suave.Filters
open Suave.Operators
open Suave.Logging
open System.Threading

open MyNamespace.helpers
open MyNamespace.Raft
open MyNamespace.dblogger
open MyNamespace.filelogger

let maxStampWebHandler (nodes:Node list) : WebPart =
    fun (cxt : HttpContext) ->  async {       
        
        let txt0=Encoding.UTF8.GetString (cxt.request.rawForm)
        let txt = JsonConvert.SerializeObject(zdblogGetMaxStamp())

        return! Successful.OK txt cxt
    }


let windowWebHandler (nodes:Node list) : WebPart =
    fun (cxt : HttpContext) ->  async {
        
        let minstamp = 
            match cxt.request.queryParam "minstamp" with
            | Choice1Of2 minstamp -> minstamp
            | _ -> "0"
        let minstamp = int minstamp

        let txt = JsonConvert.SerializeObject(zdblogWindow minstamp ( minstamp + 10000))

        return! Successful.OK txt cxt
    }


let mutable _webclientmsgid=0
let controlWebHandler (nodes:Node list) : WebPart =
    fun (cxt : HttpContext) ->  async {
        
        let action = 
            match cxt.request.queryParam "action" with
            | Choice1Of2 action -> action
            | _ -> "NONE"

        let node = 
            match cxt.request.queryParam "node" with
            | Choice1Of2 node -> node
            | _ -> "0"
        let node = (int node) - 1

        if -1<node && node<nodes.Length then
            match action with
            | "STOP"        -> nodes.[node].stop()
            | "RESUME"      -> nodes.[node].resume()
            | "RESTART"     -> nodes.[node].restart()
            | "ClientCmd"   -> 
                            _webclientmsgid <- _webclientmsgid + 1
                            let msg=new ClientMsgA("127.0.0.1:12008", System.Guid.NewGuid(), sprintf "web%A" _webclientmsgid)
                            msg.stamp <- stamp()   
                            nodes.[node].clientcmd(msg)
            | _         -> ()

        let txt = "{}"
        return! Successful.OK txt cxt
    }

let opts=InclusiveOption<string list>.All
let allcors=CORS.cors {defaultCORSConfig with allowedUris=opts; }

let dbviewer (nodes:Node list)=


    let webapp : WebPart =     
        choose [
            OPTIONS >=> pathStarts "/" >=>  allcors
            GET     >=> path "/"       >=>  Files.file "./webapp/index.html"

            GET     >=> path "/api/v1/maxstamp"   >=> allcors  >=> maxStampWebHandler nodes
            GET     >=> path "/api/v1/window"     >=> allcors  >=> windowWebHandler nodes
            GET     >=> path "/api/v1/control"    >=> allcors  >=> controlWebHandler nodes

            GET     >=> Files.browseHome
            ]

    let cts = new CancellationTokenSource()
    let conf = { 
        defaultConfig with 
            cancellationToken = cts.Token
            homeFolder = Some  (__SOURCE_DIRECTORY__.Replace("/src","").Replace("\\src","")   )        
    }

    printfn "%A" __SOURCE_DIRECTORY__

    let listening, webserver = startWebServerAsync conf webapp //(Successful.OK "Hello World")
    Async.Start(webserver, cts.Token)

    ()

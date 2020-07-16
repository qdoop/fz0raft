module MyNamespace.dbviewer

open System.Text
open Newtonsoft.Json
open Suave
open Suave.CORS
open Suave.Filters
open Suave.Operators
open Suave.Logging
open System.Threading

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
        
        
        let txt0=Encoding.UTF8.GetString (cxt.request.rawForm)

        let txt = JsonConvert.SerializeObject(zdblogWindow 0 30000)

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

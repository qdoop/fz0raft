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




let windowWebHandler (nodes:Node list) : WebPart =
    fun (cxt : HttpContext) ->  async {
        
        
        let txt0=Encoding.UTF8.GetString (cxt.request.rawForm)

        // do! Async.Sleep milliseconds
        // fzLogger "\n__QUERY_____________\n"
        // let txt1= (MyNamespace.zdemo7main.demo7evalScript txt0  fzLogger)
        // fzLogger "\n__READY_____________\n"
        // if MyNamespace.Global.REPLLOOP then
        //     let xs=MyNamespace.zterm7read.token7script txt0
        //     let ts=MyNamespace.zterm7read.parse7script xs
        //     while 0<>MyNamespace.Common.replTerms.Length do
        //         System.Threading.Thread.Sleep(0)
        //     MyNamespace.Common.replTerms <- ts

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

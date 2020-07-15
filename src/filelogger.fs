module MyNamespace.filelogger

open System.IO;
open System.Text;
open Newtonsoft.Json

open MyNamespace.globals

let fileloggers = config |> List.map ( fun x ->
            let sw = () //new StreamWriter("log" + x.Substring(x.Length - 1) + ".txt", false)
            sw 
        )

let zfilelog (id:string) stampx json =
    let obj={|
        stamp=stampx
        log  =json
    |}

    //let res=fileloggers.[(int id)-1].WriteLine( JsonConvert.SerializeObject(obj) )
    ()

type FileLine()=
    member val stamp = 0 with get, set
    member val log = "" with get, set

let zfileWindow minstamp maxstamp =
    let res0=config |> List.map ( fun x ->
        let res = seq{
                let sr = new StreamReader("log" + x.Substring(x.Length - 1) + ".txt", false)
                while 0<=sr.Peek() do
                    let line=sr.ReadLine()
                    let o=JsonConvert.DeserializeObject<FileLine>(line)
                    yield o
            }        
            //x.Find( (fun x -> minstamp<= x.stamp && x.stamp<maxstamp) )
        Seq.toList res
        )

    let res1=List.concat res0 |> List.map (fun x -> x.stamp) |> List.sort |> List.distinct

    let res={|
           stamps = res1
           ndmsgs = res0
        |}
    res
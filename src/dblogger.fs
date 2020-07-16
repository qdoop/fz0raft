module MyNamespace.dblogger

open System.Threading
open LiteDB

open MyNamespace.globals

type DbLogPOCO(id:int,stamp:int,json) =
    member val Id = id with get,set
    member val stamp = stamp with get,set
    member val log = json with get,set
    new() =  DbLogPOCO(0,0,"")


let dbzlogcollections= config |> List.map ( fun x ->
        // let conn = @"Filename=logsnode" + x.Substring(x.Length - 1) + ".db;Connection=shared"
        let conn = @"Filename=logsnode" + x.Substring(x.Length - 1) + ".db;Connection=direct"
        let db=new LiteDatabase(conn)
        let col = db.GetCollection<DbLogPOCO>("zlogs");
        // let res=col.DeleteAll()

        // let e0=new DbLogPOCO(0,0,"{xxxxx}")
        // let e1=new DbLogPOCO(0,0,"{xxxxx}")

        // let res = col.Insert(e0)
        // let res = col.Insert(e1)

        // let res = col.Count()
        // printfn "%A" res

        // let res = col.Find((fun x -> x.Id<5), 1, 1)
        // printfn "%A" res

        // let res=col.DeleteAll()
        // printfn "%A" res

        col //NOTE Returns the collection NOT the db
    )

let zdblogclear () =
    dbzlogcollections |> List.iter ( fun x  -> 
            let res=x.DeleteAll(); 
            () 
        ) 
    ()

let zdblog (id:string) stamp json =
    let res=dbzlogcollections.[(int id)-1].Insert( new DbLogPOCO(0,stamp,json) )
    ()



// for the dbviewer
let zdblogGetSizes () =
    let res=dbzlogcollections |> List.map ( fun x ->  x.Count() )
    res

let zdblogGetMaxStamp () =
    let res=dbzlogcollections |> List.map ( fun x ->
        let cnt = x.Count() 
        let res = x.Find( (fun x -> x.Id=cnt) ) 
        //printfn "%A"  res    
        if (Seq.isEmpty res) then 
            1000 
        else 
            let res =Seq.item 0 res
            res.stamp

        )
    List.max res

let zdblogWindow minstamp maxstamp =
    let res0=dbzlogcollections |> List.map ( fun x ->
        let res = x.Find( (fun x -> minstamp<= x.stamp && x.stamp<maxstamp) )
        Seq.toList res
    )
    
    let res1=List.concat res0 |> List.map (fun x -> x.stamp) |> List.sort |> List.distinct

    let res={|
           stamps = res1
           ndmsgs = res0
        |}
    res
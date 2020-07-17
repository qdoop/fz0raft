const zlog=console.log

zlog("started main.js...")




var wlogs={}

function handleWlogWindow(w){
    //zlog(w)
    let tbl=document.getElementById('tblid_wlogs')
    tbl.innerHTML=""

    var tr = document.createElement("TR");
    var th = document.createElement("TH");
    var tn = document.createTextNode( "stamp" );
    th.appendChild(tn)
    tr.appendChild(th)
    for (var j = 0; j < w.ndmsgs.length; j++) {
        var th = document.createElement("TH");
        var tn = document.createTextNode( "node" +(j+1) )
        th.appendChild(tn)
        tr.appendChild(th)
    }
    tbl.appendChild(tr)

    for (var i = 0; i < w.stamps.length; i++) {
        //zlog(w.stamps[i])
        var tr = document.createElement("TR");                 
        var tn = document.createTextNode( w.stamps[i] );
        var td=document.createElement("TD");
        td.appendChild(tn);  
        tr.appendChild(td)      
        for (var j = 0; j < w.ndmsgs.length; j++) {
            var td = document.createElement("TD");
            td.setAttribute("id", "wlog_" + w.stamps[i] + "_" + (j+1) );
            tr.appendChild(td);
        }            
        tbl.appendChild(tr)
    }

    var btnid=0;
    for (var j = 0; j < w.ndmsgs.length; j++) {
        for (var i = 0; i < w.ndmsgs[j].length; i++) {

            var e=w.ndmsgs[j][i];            
            var x=JSON.parse(e.log);
            //zlog(x)

            var td=document.getElementById("wlog_" + x.stamp + "_" +(j+1))

            var btn = document.createElement("BUTTON")
            var br = document.createElement("BR")

            var txt=x._kind
            if(0==txt.indexOf('node')){
                txt=x.state + x.stage
            }

            if(x.src && 0==x.src.indexOf('node')){
                txt=txt+">"
            }

            let id="wbtnid_" + btnid;
            btnid++;
            wlogs[id]=x

            btn.setAttribute("id", id)
            btn.addEventListener("click", function(){
                let id = this.getAttribute("id")
                zlog(wlogs[id])                
                let txt=JSON.stringify(wlogs[id],null,"  ")
                document.getElementById("tawlogid").value=txt
            }); 
            // 
            var tn = document.createTextNode( txt )
            btn.appendChild(tn)
            td.appendChild(btn)
            td.appendChild(br)

        }
    }

}

function updatewlogs(minstamp){
    fetch('/api/v1/window?minstamp='+minstamp)
    .then(response => response.json())
    .then(data => handleWlogWindow(data));
}

updatewlogs(0)
document.getElementById('minstampid').addEventListener("change", function(){
    zlog(this.value)
    updatewlogs(this.value)
})

function updateMaxStamp(){
    fetch('/api/v1/maxstamp')
    .then(response => response.json())
    .then(data => {
        //zlog(data)
        document.getElementById('minstampid').setAttribute('max',data)        
    });
    setTimeout(updateMaxStamp, 5000)
}
updateMaxStamp()


function createCtrs(count){
    var div=document.getElementById('ctrlsactionsid')
    for(var i=0; i<count; i++){

        let node=i+1;

        div.appendChild(document.createTextNode( 'node' +node))
        div.appendChild(document.createElement('BR'))
        var btn=document.createElement('BUTTON')
        var tn=document.createTextNode( 'STOP')
        btn.appendChild(tn)
        btn.addEventListener("click", function(){
            fetch('/api/v1/control?action=STOP&node='+node)
            .then(response => response.json())
        })
        div.appendChild(btn)
        div.appendChild(document.createElement('BR'))

        btn=document.createElement('BUTTON')
        tn=document.createTextNode( 'RESUME')
        btn.appendChild(tn)
        btn.addEventListener("click", function(){
            fetch('/api/v1/control?action=RESUME&node='+node)
            .then(response => response.json())
        })
        div.appendChild(btn)
        div.appendChild(document.createElement('BR'))


        btn=document.createElement('BUTTON')
        tn=document.createTextNode( 'RESTART')
        btn.appendChild(tn)
        btn.addEventListener("click", function(){
            fetch('/api/v1/control?action=RESTART&node='+node)
            .then(response => response.json())
        })
        div.appendChild(btn)
        div.appendChild(document.createElement('BR'))

        btn=document.createElement('BUTTON')
        tn=document.createTextNode( 'ClientCmd')
        btn.appendChild(tn)
        btn.addEventListener("click", function(){
            fetch('/api/v1/control?action=ClientCmd&node='+node)
            .then(response => response.json())
        })
        div.appendChild(btn)
        div.appendChild(document.createElement('BR'))

        div.appendChild(document.createElement('HR'))
    }
}
createCtrs(3)

document.getElementById('btnstepid').addEventListener("click", function(){
    var e = document.getElementById('minstampid')
    let tbl=document.getElementById('tblid_wlogs')
    let x=tbl.rows[2].cells[0]
    //zlog(x)
    e.value = x.innerHTML
    updatewlogs(e.value)
})
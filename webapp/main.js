const zlog=console.log

zlog("started main.js...")

var wlogs={}

function handleWindow(w){
    zlog(w)
    let tbl=document.getElementById('tblid_wlogs')

    for (var i = 0; i < w.stamps.length; i++) {
        zlog(w.stamps[i])
        var tr = document.createElement("TR");                 // Create a <li> node
        var textnode = document.createTextNode( w.stamps[i] );
        tr.appendChild(textnode);         // Create a text node
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
            zlog(x)

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
            }); 
            // 
            var tn = document.createTextNode( txt )
            btn.appendChild(tn)
            td.appendChild(btn)
            td.appendChild(br)

        }
    }






}

fetch('/api/v1/window')
  .then(response => response.json())
  .then(data => handleWindow(data));
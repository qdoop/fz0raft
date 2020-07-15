# fz0raft
My RAFT implementation on F#

The code is READABLE and is close to the paper spec.

The code really WORKS but some edge cases may need to fixed or added.

Three nodes are lunched as UDP endpoints exchanging true UDP packets with JSON payload the RPC msgs.

Important note: replication log compaction/snapshoting is supported and makes the code little bit harder.
See member me.logTruncate(applied)= ...

Need feedback from an expert on major implementation faults.

Tried to follow as close as possible the fig.3 Raft summary from

CONSENSUS: BRIDGING THEORY AND PRACTICE  PhD thesis of Diego Ongaro

# How to start
Just run the debuger ...

Now you have a protocol viewer. Just open http://127.0.0.1:8080/ to see the rollup

# Testing
Play with the following line inside nodeAraft.fs just to see how the 
replicated log survives random leader stops (crashes) and  resumes

                if 1 = System.Random().Next(1, 500) then
                    me.zlog <| sprintf "@@@@@@@@@@@@@@@@@@@@@@@@@@@@@@@@@@@@@@@@ KILLED!!!"
                    ...
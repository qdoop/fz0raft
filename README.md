# fz0raft
My RAFT implementation on F#

The code is READABLE and is close to the paper spec.

The code really WORKS but some edge cases may need to fixed or added.

Three nodes are lunched as UDP endpoints exchanging true UDP packets with JSON payload the RPC msgs.

Need feedback from an expert on major implementation faults.

Tried to follow as close as possible the fig.3 Raft summary from

CONSENSUS: BRIDGING THEORY AND PRACTICE  PhD thesis of Diego Ongaro

# Important notes

Last digit of port number is used as node Id. keep them 1,2,3,4,5,...

Replication log compaction/snapshoting is supported and makes the code little bit harder.
See member me.logTruncate(applied)= ...

# How to start
Just run the debuger ...

Now we have a protocol viewer. Just open http://127.0.0.1:8080/ to see the rollup

# Testing
Stop and resume the nodes from the viewer
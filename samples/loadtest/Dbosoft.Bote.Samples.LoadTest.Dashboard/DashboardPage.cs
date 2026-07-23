namespace Dbosoft.Bote.Samples.LoadTest.Dashboard;

/// <summary>Self-contained live performance page. Consolidates metrics pushed by every controller
/// replica and edge generator, and shows the live controller replica count (the scale signal).</summary>
public static class DashboardPage
{
    public const string Html = """
<!doctype html>
<html lang="en">
<head>
<meta charset="utf-8"/>
<meta name="viewport" content="width=device-width, initial-scale=1"/>
<title>Bote load test</title>
<style>
  :root { color-scheme: dark; }
  body { margin:0; font:14px/1.4 system-ui,sans-serif; background:#0f1115; color:#e6e6e6; }
  header { padding:14px 20px; background:#171a21; border-bottom:1px solid #262b36; display:flex; align-items:baseline; gap:16px; flex-wrap:wrap; }
  header h1 { font-size:16px; margin:0; font-weight:600; }
  header .sub { color:#8b93a5; font-size:12px; }
  main { padding:20px; display:grid; gap:20px; grid-template-columns:1fr 1fr; }
  @media (max-width:900px){ main{grid-template-columns:1fr;} }
  .panel { background:#171a21; border:1px solid #262b36; border-radius:10px; padding:16px; }
  .panel h2 { font-size:13px; margin:0 0 12px; color:#9aa4b8; text-transform:uppercase; letter-spacing:.5px; }
  .cards { display:grid; grid-template-columns:repeat(3,1fr); gap:10px; }
  .card { background:#0f1218; border:1px solid #232937; border-radius:8px; padding:10px 12px; }
  .card.hi { border-color:#3ddc97; }
  .card .k { color:#8b93a5; font-size:11px; }
  .card .v { font-size:20px; font-weight:600; margin-top:2px; }
  .card.hi .v { color:#3ddc97; }
  .card .u { color:#8b93a5; font-size:11px; font-weight:400; }
  .lat { margin-top:12px; font-size:12px; color:#c7cedd; }
  .lat b { color:#e6e6e6; }
  .wide { grid-column:1 / -1; }
  canvas { width:100%; height:200px; display:block; }
  .legend { display:flex; gap:16px; font-size:12px; color:#9aa4b8; margin-top:8px; flex-wrap:wrap; }
  .legend span::before { content:''; display:inline-block; width:10px; height:10px; border-radius:2px; margin-right:6px; vertical-align:middle; }
  .l-cs::before{background:#4c8dff;} .l-ea::before{background:#3ddc97;} .l-es::before{background:#f5a623;} .l-rep::before{background:#e0555f;}
</style>
</head>
<body>
<header>
  <h1>Bote load test</h1>
  <span class="sub" id="sub">connecting…</span>
  <span class="sub" id="sub-storage"></span>
</header>
<main>
  <div class="panel">
    <h2>Controller (scaled on Azure)</h2>
    <div class="cards">
      <div class="card hi"><div class="k">controller replicas</div><div class="v" id="c-rep">0</div></div>
      <div class="card"><div class="k">throughput</div><div class="v" id="c-rate">0 <span class="u">msg/s</span></div></div>
      <div class="card"><div class="k">edges registered</div><div class="v" id="c-edges">0</div></div>
      <div class="card"><div class="k">small / s</div><div class="v" id="c-small">0</div></div>
      <div class="card"><div class="k">large / s</div><div class="v" id="c-large">0</div></div>
      <div class="card"><div class="k">total handled</div><div class="v" id="c-total">0</div></div>
    </div>
    <div class="lat">handle latency — small: <b id="c-slat">–</b> · large: <b id="c-llat">–</b></div>
  </div>

  <div class="panel">
    <h2>Edge (round-trip)</h2>
    <div class="cards">
      <div class="card"><div class="k">edge generators</div><div class="v" id="e-gen">0</div></div>
      <div class="card"><div class="k">small ack / s</div><div class="v" id="e-sack">0</div></div>
      <div class="card"><div class="k">in-flight</div><div class="v" id="e-inflight">0</div></div>
      <div class="card"><div class="k">small sent / s</div><div class="v" id="e-ssent">0</div></div>
      <div class="card"><div class="k">large echo / s</div><div class="v" id="e-lecho">0</div></div>
      <div class="card"><div class="k">errors</div><div class="v" id="e-err">0</div></div>
    </div>
    <div class="lat">round-trip — small: <b id="e-slat">–</b> · large: <b id="e-llat">–</b></div>
  </div>

  <div class="panel wide">
    <h2>Noisy neighbor — do the flooders hurt the quiet clients?</h2>
    <div class="cards" style="grid-template-columns:repeat(4,1fr);">
      <div class="card hi"><div class="k">quiet round-trip p95</div><div class="v" id="n-qlat">– <span class="u">ms</span></div></div>
      <div class="card"><div class="k">noisy round-trip p95</div><div class="v" id="n-nlat">– <span class="u">ms</span></div></div>
      <div class="card"><div class="k">noisy sent / s</div><div class="v" id="n-nrate">0</div></div>
      <div class="card"><div class="k">deferred / s</div><div class="v" id="n-defer">0</div></div>
    </div>
    <div class="lat">quiet: <b id="n-qfull">–</b><br/>noisy: <b id="n-nfull">–</b></div>
  </div>

  <div class="panel wide">
    <h2>Throughput (messages / s) &amp; cloud-queue backlog</h2>
    <canvas id="chart" width="1200" height="200"></canvas>
    <div class="legend">
      <span class="l-cs">controller handled</span>
      <span class="l-ea">edge acked</span>
      <span class="l-rep">controller replicas (×10)</span>
    </div>
  </div>
</main>

<script>
const $ = id => document.getElementById(id);
const hist = { cHandled:[], eAcked:[], reps:[] };
const MAXP = 90;
let prev = null;
const z = () => ({count:0,avgMs:0,p95Ms:0,maxMs:0});

function lat(s){ return s && s.count ? `avg ${s.avgMs} · p95 ${s.p95Ms} · max ${s.maxMs} ms (${s.count})` : '–'; }
function aggEdge(reports){
  const a = { edges:0, smallSent:0, smallAcked:0, largeSent:0, largeEchoed:0, smallInFlight:0, largeInFlight:0, errors:0,
              quietSent:0, noisySent:0,
              smallRoundtrip:z(), largeRoundtrip:z(), quietRoundtrip:z(), noisyRoundtrip:z() };
  for(const r of reports||[]){
    a.edges+=r.edges||0; a.smallSent+=r.smallSent||0; a.smallAcked+=r.smallAcked||0;
    a.largeSent+=r.largeSent||0; a.largeEchoed+=r.largeEchoed||0;
    a.smallInFlight+=r.smallInFlight||0; a.largeInFlight+=r.largeInFlight||0; a.errors+=r.errors||0;
    a.quietSent+=r.quietSent||0; a.noisySent+=r.noisySent||0;
    for(const [k,src] of [['smallRoundtrip',r.smallRoundtrip],['largeRoundtrip',r.largeRoundtrip],
                          ['quietRoundtrip',r.quietRoundtrip],['noisyRoundtrip',r.noisyRoundtrip]]){
      if(src){ a[k].count+=src.count||0; a[k].avgMs=Math.max(a[k].avgMs,src.avgMs||0); a[k].p95Ms=Math.max(a[k].p95Ms,src.p95Ms||0); a[k].maxMs=Math.max(a[k].maxMs,src.maxMs||0); }
    }
  }
  return a;
}
function push(arr,v){ arr.push(v); if(arr.length>MAXP) arr.shift(); }

function draw(){
  const cv=$('chart'), ctx=cv.getContext('2d'); const W=cv.width,H=cv.height;
  ctx.clearRect(0,0,W,H);
  const series=[[hist.cHandled,'#4c8dff'],[hist.eAcked,'#3ddc97'],[hist.reps,'#e0555f']];
  let max=1; for(const [s] of series) for(const v of s) if(v>max) max=v;
  ctx.strokeStyle='#232937'; ctx.lineWidth=1;
  for(let g=0; g<=4; g++){ const y=H- (H-20)*g/4 -10; ctx.beginPath(); ctx.moveTo(0,y); ctx.lineTo(W,y); ctx.stroke();
    ctx.fillStyle='#5a6272'; ctx.font='10px sans-serif'; ctx.fillText(Math.round(max*g/4), 2, y-2); }
  for(const [s,color] of series){
    ctx.strokeStyle=color; ctx.lineWidth=2; ctx.beginPath();
    s.forEach((v,i)=>{ const x=W*i/(MAXP-1); const y=H-10-(H-20)*(v/max); i?ctx.lineTo(x,y):ctx.moveTo(x,y); });
    ctx.stroke();
  }
}

async function tick(){
  let m;
  try { m = await (await fetch('/metrics')).json(); }
  catch { $('sub').textContent='dashboard unreachable'; return; }
  const c = m.controller, e = aggEdge(m.edgeReports);
  const now = m.serverTimeMs;
  let cH=0,eA=0;
  if(prev){
    const dt=(now-prev.t)/1000 || 1;
    const rate=(a,b)=> Math.max(0, Math.round((a-b)/dt));
    cH=rate(c.smallHandled+c.largeHandled, prev.cHandled);
    eA=rate(e.smallAcked+e.largeEchoed, prev.eAcked);
    $('c-rate').innerHTML=cH+' <span class="u">msg/s</span>';
    $('c-small').textContent=rate(c.smallHandled, prev.cSmall);
    $('c-large').textContent=rate(c.largeHandled, prev.cLarge);
    $('e-sack').textContent=eA;
    $('e-ssent').textContent=rate(e.smallSent, prev.eSmallSent);
    $('e-lecho').textContent=rate(e.largeEchoed, prev.eLargeEcho);
    $('n-nrate').textContent=rate(e.noisySent, prev.eNoisySent);
    $('n-defer').textContent=rate(c.deferred||0, prev.cDeferred||0);
  }
  $('n-qlat').innerHTML=(e.quietRoundtrip.count? e.quietRoundtrip.p95Ms : '–')+' <span class="u">ms</span>';
  $('n-nlat').innerHTML=(e.noisyRoundtrip.count? e.noisyRoundtrip.p95Ms : '–')+' <span class="u">ms</span>';
  $('n-qfull').textContent=lat(e.quietRoundtrip);
  $('n-nfull').textContent=lat(e.noisyRoundtrip);
  $('c-rep').textContent=c.replicas;
  // Edge count comes from the edge generators (reliable), not the controller's per-replica
  // RegisterEdge tally, which breaks under competing consumers + scaling (one-time registration).
  $('c-edges').textContent=e.edges;
  $('c-total').textContent=c.smallHandled+c.largeHandled;
  $('c-slat').textContent=lat(c.smallLatency);
  $('c-llat').textContent=lat(c.largeLatency);
  $('e-gen').textContent=m.edgeGenerators;
  $('e-inflight').textContent=e.smallInFlight+e.largeInFlight;
  $('e-err').textContent=e.errors;
  $('e-slat').textContent=lat(e.smallRoundtrip);
  $('e-llat').textContent=lat(e.largeRoundtrip);
  $('sub').textContent=`${c.replicas} controller replica(s) · ${m.edgeGenerators} generator(s) · ${e.edges} edges · updated ${new Date().toLocaleTimeString()}`;
  const st=m.storage;
  $('sub-storage').textContent = st
    ? `· cloudQ ${st.cloudQueueDepth ?? 'n/a'} · clientsQ ${st.clientsQueueDepth ?? 'n/a'} · edge-poll-wait ${st.edgeQueueDepth} (${st.edgeQueues} q)`
    : '· storage: sampling…';
  push(hist.cHandled,cH); push(hist.eAcked,eA); push(hist.reps,(c.replicas||0)*10); draw();
  prev={ t:now, cHandled:c.smallHandled+c.largeHandled, cSmall:c.smallHandled, cLarge:c.largeHandled,
         eAcked:e.smallAcked+e.largeEchoed, eSmallSent:e.smallSent, eLargeEcho:e.largeEchoed,
         eNoisySent:e.noisySent, cDeferred:c.deferred||0 };
}
setInterval(tick, 1000); tick();
</script>
</body>
</html>
""";
}

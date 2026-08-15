const puppeteer = require('puppeteer');
const fs = require('fs'), path = require('path');

// 42 decos (sin sub_)
const dir = 'Assets/ScriptableObjects/Decorations';
let ids = fs.readdirSync(dir).filter(f=>f.endsWith('.asset')).map(f=>{
  const m = fs.readFileSync(path.join(dir,f),'utf8').match(/^\s*itemId:\s*(.+)$/m); return m?m[1].trim():null;
}).filter(Boolean).filter(id=>!id.startsWith('sub_')).sort();

const baseHtml = fs.readFileSync('webgl-output/index.html','utf8');
const BATCH = 7;
const results = {}; const failed = [];

function mkPage(batch){
  let arr='';
  batch.forEach((id,i)=>{ arr+=`          { itemId: '${id}', instanceId: '${id}_0', position: {x:${(-4+i*1.3).toFixed(1)}, y:-2.8, z:2.0}, scaleFactor: 0.6 },\n`; });
  const html = baseHtml.replace(/(var testDecos = \{ items: \[)[\s\S]*?(\]\};)/, '$1\n'+arr+'        $2');
  fs.writeFileSync('webgl-output/_audit.html', html);
}

(async () => {
  for (let b=0; b<ids.length; b+=BATCH){
    const batch = ids.slice(b, b+BATCH);
    mkPage(batch);
    const browser = await puppeteer.launch({ headless:true, args:['--no-sandbox','--disable-setuid-sandbox','--enable-webgl','--use-gl=angle','--ignore-gpu-blacklist'] });
    const page = await browser.newPage();
    const fix=[], errs=[], placed=[];
    page.on('console', m=>{ const t=m.text();
      if(/FixMat /.test(t)) fix.push(t);
      if(/No Location|deco load FAILED|deco not found/.test(t)) errs.push(t);
      if(/Colocado:/.test(t)) placed.push(t);
    });
    try{
      await page.goto('http://localhost:3001/_audit.html?devtest=1',{waitUntil:'domcontentloaded',timeout:30000});
      await page.waitForFunction(()=>!!window.unityInstance,{timeout:90000,polling:500}).catch(()=>{});
      let done=false;
      for(let i=0;i<25;i++){ await new Promise(r=>setTimeout(r,2000)); const p=await page.$eval('#dbg-panel',e=>e.innerText).catch(()=>''); if(/Decos placed/.test(p)){done=true;break;} }
      await new Promise(r=>setTimeout(r,1500));
    }catch(e){}
    // parsear FixMat: "FixMat <prefab>(Clone): mat=<m> shader=<s>"
    for(const l of fix){ const m=l.match(/FixMat ([^:]+?)\(Clone\): mat=(\S+) shader=(.+)$/); if(m){ const pf=m[1].trim(); (results[pf]=results[pf]||new Set()).add(m[3].trim()); } }
    for(const e of errs){ const m=e.match(/(?:No Location found for Key=|deco (?:load FAILED|not found):?\s*)(\S+)/); if(m) failed.push(m[1].replace(/_0$/,'')); }
    console.error(`batch ${b/BATCH+1}: ${batch.length} decos, placed=${placed.length}, fixMat=${fix.length}, err=${errs.length}`);
    await browser.close();
  }
  fs.rmSync('webgl-output/_audit.html',{force:true});
  // Clasificar
  const SAFE=/FishUnlit|Sprites/, FIXED=/Universal Render Pipeline\/Lit/, RISK=/Standard|glTF|gltf/;
  console.log('\n===== INFORME DECOS (prefab → shaders) =====');
  for(const pf of Object.keys(results).sort()){
    const sh=[...results[pf]]; let tag='?';
    if(sh.some(s=>RISK.test(s))) tag='⚠ RIESGO';
    else if(sh.every(s=>SAFE.test(s))) tag='✅ safe';
    else if(sh.some(s=>FIXED.test(s))) tag='✅ fix→FishUnlit';
    console.log(`${tag.padEnd(16)} ${pf.padEnd(34)} ${sh.join(' | ')}`);
  }
  if(failed.length) console.log('\n⚠ NO CARGARON: '+[...new Set(failed)].join(', '));
})();

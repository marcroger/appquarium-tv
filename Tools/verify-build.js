const puppeteer = require('puppeteer');
(async () => {
  const browser = await puppeteer.launch({ headless:true, args:['--no-sandbox','--disable-setuid-sandbox','--enable-webgl','--use-gl=angle','--ignore-gpu-blacklist','--window-size=1920,1080'] });
  const page = await browser.newPage();
  await page.setViewport({ width:1920, height:1080 });
  const sig = {bloom:'', rscale:'', fix:[], placed:''};
  page.on('console', m=>{ const t=m.text();
    if(/PostFX.*bloom|bloom=/.test(t) && !sig.bloom) sig.bloom=t.match(/bloom=\S+/)?.[0]||'';
    if(/renderScale set/.test(t)) sig.rscale=t.replace(/^\[[0-9.]+s\] \[C#\] /,'');
    if(/FixMat /.test(t)) sig.fix.push(t.replace(/^\[[0-9.]+s\] \[C#\] /,''));
    if(/Decos placed/.test(t)) sig.placed=t.replace(/^\[[0-9.]+s\] \[C#\] /,'');
  });
  await page.goto('http://localhost:3001/?devtest=1',{waitUntil:'domcontentloaded',timeout:30000});
  await page.waitForFunction(()=>{var p=document.getElementById('dbg-panel');return p&&/Decos placed/.test(p.innerText);},{timeout:90000,polling:500}).catch(()=>{});
  await new Promise(r=>setTimeout(r,3000));
  await page.evaluate(()=>{var p=document.getElementById('dbg-panel');if(p)p.style.display='none';var f=document.getElementById('fps-meter');if(f)f.style.display='none';});
  await new Promise(r=>setTimeout(r,400));
  await page.screenshot({path:'deco-floor.png',clip:{x:200,y:740,width:1400,height:320}});
  // dedup FixMat por shader final
  const seen=new Set(),uniq=[];
  for(const l of sig.fix){const m=l.match(/FixMat ([^(]+)\(Clone\): mat=(\S+) shader=(.+)/);if(m){const k=m[1].trim();if(!seen.has(k)){seen.add(k);uniq.push(`${m[1].trim()} → ${m[3].trim()}`);}}}
  console.log('BLOOM:', sig.bloom||'(no encontrado)');
  console.log('RENDERSCALE:', sig.rscale||'(no encontrado)');
  console.log('PLACED:', sig.placed);
  console.log('FIXMAT (shader ORIGINAL pre-fix, por prefab):');
  console.log(uniq.join('\n'));
  await browser.close();
})();

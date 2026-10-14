// Packs separated generated figures into a regular atlas; never synthesizes artwork.
// Usage: node Tools/Art/PackHeroSprites.cjs <source-directory> <output-directory>
// Requires sharp in the art-tool environment, not in the Unity runtime.
const fs = require('node:fs');
const path = require('node:path');
const sharp = require('sharp');
const source = path.resolve(process.argv[2]);
const output = path.resolve(process.argv[3]);
const names = ['warrior', 'rogue', 'ranger', 'mage'];
const cell = 256, baseline = 224;

function separator(counts, center, radius) {
  const start = Math.max(1, Math.floor(center - radius));
  const end = Math.min(counts.length - 1, Math.ceil(center + radius));
  let best = null;
  for (let x = start; x < end; x++) {
    if (counts[x] !== 0) continue;
    let right = x;
    while (right < end && counts[right] === 0) right++;
    if (!best || right - x > best.length) best = {point: Math.floor((x + right) / 2), length: right - x};
    x = right;
  }
  if (!best || best.length < 2) throw new Error('No clean separator near ' + center);
  return best.point;
}

(async () => {
  fs.mkdirSync(output, {recursive:true});
  const report = [];
  for (const name of names) {
    const file = path.join(source, name + '.png');
    const {data,info} = await sharp(file).ensureAlpha().raw().toBuffer({resolveWithObject:true});
    const {width,height} = info;
    // Generated transparent gutters contain nearly invisible alpha noise.
    for(let i=3;i<data.length;i+=4) if(data[i]<=16) data[i]=0;
    const clean=await sharp(data,{raw:{width,height,channels:4}}).png().toBuffer();
    const alpha = (x,y) => data[(y * width + x) * 4 + 3];
    const ys = Array(height).fill(0);
    for(let y=0;y<height;y++) for(let x=0;x<width;x++) if(alpha(x,y)>0) ys[y]++;
    const rows=[0];
    for(let row=1;row<5;row++) rows.push(separator(ys,height*row/5,height*.065));
    rows.push(height);
    const frames=[];
    for(let row=0;row<5;row++) {
      const xs=Array(width).fill(0);
      for(let y=rows[row];y<rows[row+1];y++) for(let x=0;x<width;x++) if(alpha(x,y)>0) xs[x]++;
      const cols=[0];
      for(let col=1;col<4;col++) cols.push(separator(xs,width*col/4,width*.07));
      cols.push(width);
      for(let col=0;col<4;col++) {
        let minX=width,minY=height,maxX=-1,maxY=-1;
        for(let y=rows[row];y<rows[row+1];y++) for(let x=cols[col];x<cols[col+1];x++) if(alpha(x,y)>0) {
          minX=Math.min(minX,x);minY=Math.min(minY,y);maxX=Math.max(maxX,x);maxY=Math.max(maxY,y);
        }
        if(maxX<minX) throw new Error(name+' empty frame');
        // Align the lower body, not the sword/bow/spell extending to one side.
        let footMin=width,footMax=-1;
        const footTop=maxY-Math.round((maxY-minY)*.12);
        for(let y=footTop;y<=maxY;y++)for(let x=minX;x<=maxX;x++)if(alpha(x,y)>32){footMin=Math.min(footMin,x);footMax=Math.max(footMax,x);}
        frames.push({row,col,left:minX,top:minY,width:maxX-minX+1,height:maxY-minY+1,anchor:(footMin+footMax)/2-minX});
      }
    }
    // A single scale for every pose preserves size during attacks and recoils.
    const idleHeight=frames.slice(0,4).reduce((n,f)=>n+f.height,0)/4;
    let scale=200/idleHeight;
    for(const f of frames)scale=Math.min(scale,212/f.height,112/Math.max(f.anchor,f.width-f.anchor));
    const images=[];
    for(const f of frames){
      const w=Math.round(f.width*scale),h=Math.round(f.height*scale);
      const left=Math.round(128-f.anchor*scale),top=baseline-h;
      if(left<8||top<8||left+w>248)throw new Error(name+' frame does not fit');
      images.push({input:await sharp(clean).extract({left:f.left,top:f.top,width:f.width,height:f.height})
        .resize(w,h,{kernel:'nearest'}).png().toBuffer(),left:f.col*cell+left,top:f.row*cell+top});
    }
    const target=path.join(output,'hero-'+name+'-v2.png');
    await sharp({create:{width:cell*4,height:cell*5,channels:4,background:{r:0,g:0,b:0,alpha:0}}}).composite(images).png().toFile(target);
    report.push({name,sourceSize:[width,height],atlasSize:[1024,1280],scale,frames});
    console.log(name+': 20 complete figures, clean separators, regular 256px cells -> '+target);
  }
  fs.writeFileSync(path.join(source,'packing-report.json'),JSON.stringify(report,null,2)+'\n');
})().catch(error=>{console.error(error);process.exitCode=1;});

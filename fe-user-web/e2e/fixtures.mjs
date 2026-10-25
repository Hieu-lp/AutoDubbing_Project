export const codes=['vi','en','zh-hans','ja','ko','fr','de','es','ru','it','pt','th','id','ms','hi'];
const names=['Nhà thờ Đức Bà','Bưu điện Trung tâm Sài Gòn','Dinh Độc Lập','Bảo tàng Chứng tích Chiến tranh','Chợ Bến Thành','Nhà hát Thành phố','Phố đi bộ Nguyễn Huệ','Thảo Cầm Viên Sài Gòn'];
export const pois=names.map((name,index)=>({id:index+1,name,latitude:10.7798+index*.0009,longitude:106.699,
  originalLanguage:'vi',originalDescription:`Fixture nguồn ${index+1}`,proximityRadius:123,thumbnailUrl:null}));
export function stored(id,language,ready=true) {
  const actual=ready?language:'vi';
  return {poiId:id,requestedLanguage:language,actualLanguage:actual,isFallback:actual!==language,
    text:ready?`Fixture ${id} - ${language}`:`Fixture nguồn ${id}`,
    audio:ready?{id,language,url:`https://audio.fixture.invalid/${id}/${language}.wav`}:null};
}
export function prepared(id,language,status='Ready') {
  return {poiId:id,language,sourceVersion:'fixture-source',status,textReady:status==='Ready'||language==='vi',audioReady:status==='Ready',
    content:stored(id,language,status==='Ready'),jobId:status==='Queued'?'fixture-job':null,statusUrl:status==='Queued'?'/api/content-preparations/fixture-job':null,
    errorCode:status==='Unavailable'?'GenerationPaused':null,retryAt:null,requiresOperatorAction:false};
}
function waveBytes() {
  const data=Buffer.alloc(48044);data.write('RIFF',0);data.writeUInt32LE(48036,4);data.write('WAVEfmt ',8);data.writeUInt32LE(16,16);
  data.writeUInt16LE(1,20);data.writeUInt16LE(1,22);data.writeUInt32LE(24000,24);data.writeUInt32LE(48000,28);
  data.writeUInt16LE(2,32);data.writeUInt16LE(16,34);data.write('data',36);data.writeUInt32LE(48000,40);return data;
}
export async function setup(page,{gps=true,...options}={}) {
  const world={posts:[],routes:[],polls:0,reads:[],ready:true,routeError:false,readDelay:{},...options};
  await page.addInitScript(({gps})=>{
    window.__plays=[];window.__blockAudio=false;window.__cleared=0;
    const playing=new WeakMap();
    Object.defineProperty(HTMLMediaElement.prototype,'paused',{get(){return !playing.get(this);}});
    HTMLMediaElement.prototype.play=function(){window.__plays.push(this.src);if(window.__blockAudio)return Promise.reject(new DOMException('Blocked','NotAllowedError'));
      playing.set(this,true);this.dispatchEvent(new Event('play'));return Promise.resolve();};
    HTMLMediaElement.prototype.pause=function(){playing.set(this,false);this.dispatchEvent(new Event('pause'));};
    if(gps){
      const watchers=new Map();let id=0;
      window.__gpsCallbacks=[];
      navigator.geolocation.watchPosition=(success,error)=>{watchers.set(++id,{success,error});window.__gpsCallbacks.push(success);return id;};
      navigator.geolocation.clearWatch=id=>{watchers.delete(id);window.__cleared++;};
      navigator.geolocation.getCurrentPosition=success=>{
        window.__resolveLocate=()=>success({coords:{latitude:10.7798,longitude:106.699,accuracy:5},timestamp:Date.now()});
        if(!window.__holdLocate)window.__resolveLocate();
      };
      window.__gps=(latitude=10.7798,longitude=106.699,accuracy=5,age=0)=>{for(const watcher of watchers.values())watcher.success({coords:{latitude,longitude,accuracy},timestamp:Date.now()-age});};
      window.__gpsError=code=>{for(const watcher of watchers.values())watcher.error({code});};
    }
  },{gps});
  await page.route('**/*.tile.openstreetmap.org/**',route=>route.abort());
  await page.route('https://fonts.googleapis.com/**',route=>route.abort());
  await page.route('https://fonts.gstatic.com/**',route=>route.abort());
  await page.route('https://audio.fixture.invalid/**',route=>route.fulfill({body:waveBytes(),contentType:'audio/wav'}));
  await page.route('**/api/**',async route=>{
    const request=route.request();const url=new URL(request.url());const path=url.pathname;const body=request.postDataJSON();
    const respond=(json,status=200,headers={})=>route.fulfill({status,contentType:'application/json',headers,body:JSON.stringify(json)}).catch(()=>{});
    if(path==='/api/pois'&&world.listError)return respond({title:'Fixture unavailable'},500);
    if(path==='/api/pois')return respond(pois);
    if(path==='/api/languages')return respond(codes.map(code=>({code,displayName:code})));
    if(path==='/api/content-preparations/settings')return respond({preparationRadiusMeters:500,playbackRadiusMeters:50,dwellSeconds:3,pollAfterSeconds:3});
    if(path==='/api/pois/prepare-nearby'){
      world.posts.push({path,body});
      if(world.nearbyDelay)await new Promise(resolve=>setTimeout(resolve,world.nearbyDelay));
      return respond({preparationRadiusMeters:500,items:[prepared(1,'vi',world.ready?'Ready':'Queued'),prepared(2,'vi','Unavailable')]});
    }
    if(path==='/api/routes/preview'){
      world.routes.push(body);
      if(world.routeError)return respond({code:'routing_unavailable',detail:'Fixture ORS không phản hồi.'},502);
      return respond({start:{latitude:body.startLatitude,longitude:body.startLongitude},stops:[{...pois[0],poiId:1,order:1},{...pois[1],poiId:2,order:2}],
        geometry:{type:'LineString',coordinates:[[106.699,10.7798],[106.6993,10.7801],[106.699,10.7807]]},
        totalDistanceMeters:1234,totalDurationSeconds:777,attribution:'© ORS fixture / OSM',distanceType:'walking',provider:'openrouteservice'});
    }
    if(path.startsWith('/api/content-preparations/')){world.polls++;return respond(prepared(1,'vi',world.ready?'Ready':'Queued'),200,{'Retry-After':'3'});}
    const match=path.match(/^\/api\/pois\/(\d+)(?:\/(content|prepare-content))?$/);
    if(match){const id=Number(match[1]);if(!match[2])return respond(pois[id-1]);
      const language=url.searchParams.get('language')??body?.language;
      if(match[2]==='content'){
        world.reads.push(`${id}:${language}`);
        const delay=world.readDelay[`${id}:${language}`];if(delay)await new Promise(resolve=>setTimeout(resolve,delay));
        return respond(stored(id,language,id===1&&world.ready));
      }
      world.posts.push({path,body});
      if(world.rateLimited)return respond({code:'RequestRateLimited',title:'Fixture rate limit'},429,{'Retry-After':'60'});
      if(world.accepted)return respond(prepared(id,language,world.ready?'Ready':'Queued'),world.ready?200:202,{'Retry-After':'3'});
      return respond(prepared(id,language,'Unavailable'),503);
    }
    return respond({title:'Unexpected fixture request'},404);
  });
  return world;
}
export async function open(page) {await page.goto('/');await page.getByRole('button',{name:'VI',exact:true}).click();await page.locator('.poi-list .poi-row').first().waitFor();}
export const select=(page,name='Nhà thờ Đức Bà')=>page.locator('.poi-list').getByRole('button',{name:new RegExp(name)}).click();

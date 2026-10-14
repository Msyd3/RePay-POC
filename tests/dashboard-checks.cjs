const fs=require('fs'),vm=require('vm'),assert=require('assert');
const html=fs.readFileSync(require('path').join(__dirname,'../wwwroot/analytics.html'),'utf8');
const ids=[...html.matchAll(/id="([^"]+)"/g)].map(m=>m[1]);
function element(){return {textContent:'',hidden:false,disabled:false,value:'',children:[],style:{},attributes:{},setAttribute(k,v){this.attributes[k]=v},querySelector(k){return this.parts[k]},parts:{'.spinner':{hidden:true},'.button-label':{textContent:''}},append(c){this.children.push(c)},appendChild(c){this.children.push(c)},replaceChildren(){this.children=[]}}}
const elements=Object.fromEntries(ids.map(id=>[id,element()]));
for(const id of ['dashboard','actions'])elements[id].hidden=true;
let resolveRequest;
const context={document:{body:{className:"signed-out"},getElementById:id=>{assert(elements[id],id);return elements[id]},createElement:element},Intl,Date,AbortController,setTimeout,clearTimeout,fetch:url=>url.includes('login-mode')?Promise.resolve({ok:true,json:async()=>({phone:false})}):new Promise(resolve=>{resolveRequest=resolve})};
vm.createContext(context);vm.runInContext(html.match(/<script>([\s\S]*?)<\/script>/)[1],context);
const fixture={users:2,conversations:4,transfers:6,totalAmounts:120,averageAmounts:40,averageRepeat:1,updatedAt:'2026-10-14T12:00:00Z',userList:[{name:'Test user',mobile:'050 XXXX 0000',operations:4,repeats:2,conversations:3}]};
(async()=>{
let pending=context.refresh();
assert(elements.refresh.disabled);assert.equal(elements.refresh.attributes['aria-busy'],'true');assert(!elements.refresh.parts['.spinner'].hidden);assert.equal(elements.refresh.parts['.button-label'].textContent,'جارٍ التحميل…');
resolveRequest({ok:true,json:async()=>fixture});await pending;
assert.equal(elements.heading.textContent,'ري باي بالأرقام');assert(elements.login.hidden);assert(!elements.dashboard.hidden);assert.equal(elements.totalAmounts.textContent,'120.00');assert.equal(elements.userRows.children[0].children.length,5);assert.equal(elements.userRows.children[0].children[1].className,'mobile');assert(!elements.refresh.disabled);assert(elements.refresh.parts['.spinner'].hidden);
pending=context.refresh();resolveRequest({ok:false,status:500});await pending;assert(!elements.dashboard.hidden);assert(elements.error.textContent.includes('تعذر'));assert.equal(elements.totalAmounts.textContent,'120.00');
pending=context.refresh();resolveRequest({ok:false,status:401});await pending;assert(elements.dashboard.hidden);assert(!elements.login.hidden);assert.equal(elements.heading.textContent,'الدخول في لوحة التحكم');
pending=context.refresh();resolveRequest({ok:true,json:async()=>({...fixture,userList:[]})});await pending;assert.equal(elements.userRows.children[0].children[0].colSpan,5);
elements.logout.onclick();assert.equal(elements.userRows.children.length,0);assert(elements.dashboard.hidden);assert.equal(elements.heading.textContent,'الدخول في لوحة التحكم');
assert(!html.includes('تجارب البحث'));assert(!html.includes('تجارب الدفع'));assert(!html.includes('العمليات المالية المنفّذة'));assert(!html.includes('المحادثات، بالأرقام.'));
console.log('PASS dashboard loading, metrics, table, refresh errors, authentication, empty state and logout');
})().catch(e=>{console.error(e);process.exitCode=1});

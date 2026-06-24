import{i as e}from"./preload-helper-xPQekRTU.js";import{c as t}from"./iframe-CSAmYpUG.js";import{a as n,c as r,i,l as a,n as o,r as s,t as c,u as l}from"./App-DkjXwAIB.js";import{i as u,r as d}from"./sideportFixtures-CaLkdlhA.js";var f,p,m,h,g,_,v,y,b,x,S,C,w,T,E,D,O,k,A,j,M,N,P,F,I,L,R,z,B,V,H;e((()=>{l(),u(),f=t(),p={title:`Sideport/Components`,decorators:[e=>(0,f.jsx)(`div`,{className:`story-pad`,children:(0,f.jsx)(e,{})})],parameters:{layout:`fullscreen`,docs:{description:{component:`Per-component state matrix from the design spec. Each story exercises a hard-to-reach state in isolation, so empty / blocked / failed / full paths can be reviewed without driving the whole shell.`}}}},m={mode:`live`,baseUrl:`/sideport-api`,message:`Live API`,canMutate:!0},h=e=>d.apps.filter(t=>t.deviceUdid===e).map(t=>({...t,deviceUdid:e})),g=(e,t)=>d.apps.slice(0,e).map((e,n)=>({...e,deviceUdid:t,bundleId:`${e.bundleId}.${n}`})),_=d.devices[0],v=d.devices[1],y=d.devices[2],b={..._,udid:`BLOCKED-UDID`,name:`Blocked iPhone`,health:`blocked`,connection:`usb`,blocker:`Provisioning profile expired.`},x={...v,udid:`FULL-UDID`,name:`Full iPhone`,appSlotsUsed:3},S={name:`DeviceCard — Wi-Fi`,render:()=>(0,f.jsx)(`div`,{className:`story-card`,children:(0,f.jsx)(o,{device:_,apps:h(_.udid)})})},C={name:`DeviceCard — USB`,render:()=>(0,f.jsx)(`div`,{className:`story-card`,children:(0,f.jsx)(o,{device:v,apps:h(v.udid)})})},w={name:`DeviceCard — offline`,render:()=>(0,f.jsx)(`div`,{className:`story-card`,children:(0,f.jsx)(o,{device:y,apps:[]})})},T={name:`DeviceCard — blocked`,render:()=>(0,f.jsx)(`div`,{className:`story-card`,children:(0,f.jsx)(o,{device:b,apps:h(_.udid)})})},E={name:`DeviceCard — 3/3 slots`,render:()=>(0,f.jsx)(`div`,{className:`story-card`,children:(0,f.jsx)(o,{device:x,apps:g(3,x.udid)})})},D={name:`AppSlotGrid — 0/3 empty`,render:()=>(0,f.jsx)(c,{apps:[],canRegister:!0})},O={name:`AppSlotGrid — 1/3 used`,render:()=>(0,f.jsx)(c,{apps:g(1,`demo-device`),canRegister:!0})},k={name:`AppSlotGrid — 3/3 full`,render:()=>(0,f.jsx)(c,{apps:g(3,`demo-device`),canRegister:!1})},A={name:`RenewalQueue — operation states`,render:()=>(0,f.jsx)(i,{items:d.renewals,apps:d.apps,apiStatus:m})},j={name:`RenewalQueue — blocked`,render:()=>(0,f.jsx)(i,{items:d.renewals.filter(e=>e.risk===`blocked`),apps:d.apps,apiStatus:m})},M={name:`RenewalQueue — empty`,render:()=>(0,f.jsx)(i,{items:[],apps:d.apps,apiStatus:m})},N={name:`Diagnostics — all categories`,render:()=>(0,f.jsx)(s,{issues:d.issues})},P={name:`Diagnostics — install failed`,render:()=>(0,f.jsx)(s,{issues:d.issues.filter(e=>e.category.includes(`Install`))})},F={name:`Diagnostics — resolved`,render:()=>(0,f.jsx)(s,{issues:d.issues.filter(e=>e.status===`resolved`)})},I=[`healthy`,`warning`,`blocked`,`failed`,`offline`],L=[`live`,`derived`,`demo`,`planned`],R=[`owner`,`admin`,`operator`,`viewer`],z={name:`StatusPill — all states`,render:()=>(0,f.jsx)(`div`,{className:`story-row`,children:I.map(e=>(0,f.jsx)(a,{state:e,label:e},e))})},B={name:`SourcePill — all sources`,render:()=>(0,f.jsx)(`div`,{className:`story-row`,children:L.map(e=>(0,f.jsx)(r,{source:e,label:e},e))})},V={name:`RoleBadge — all roles`,render:()=>(0,f.jsx)(`div`,{className:`story-row`,children:R.map(e=>(0,f.jsx)(n,{role:e},e))})},S.parameters={...S.parameters,docs:{...S.parameters?.docs,source:{originalSource:`{
  name: 'DeviceCard — Wi-Fi',
  render: () => <div className="story-card"><DeviceCard device={wifiDevice} apps={appsFor(wifiDevice.udid)} /></div>
}`,...S.parameters?.docs?.source}}},C.parameters={...C.parameters,docs:{...C.parameters?.docs,source:{originalSource:`{
  name: 'DeviceCard — USB',
  render: () => <div className="story-card"><DeviceCard device={usbDevice} apps={appsFor(usbDevice.udid)} /></div>
}`,...C.parameters?.docs?.source}}},w.parameters={...w.parameters,docs:{...w.parameters?.docs,source:{originalSource:`{
  name: 'DeviceCard — offline',
  render: () => <div className="story-card"><DeviceCard device={offlineDevice} apps={[]} /></div>
}`,...w.parameters?.docs?.source}}},T.parameters={...T.parameters,docs:{...T.parameters?.docs,source:{originalSource:`{
  name: 'DeviceCard — blocked',
  render: () => <div className="story-card"><DeviceCard device={blockedDevice} apps={appsFor(wifiDevice.udid)} /></div>
}`,...T.parameters?.docs?.source}}},E.parameters={...E.parameters,docs:{...E.parameters?.docs,source:{originalSource:`{
  name: 'DeviceCard — 3/3 slots',
  render: () => <div className="story-card"><DeviceCard device={fullDevice} apps={nApps(3, fullDevice.udid)} /></div>
}`,...E.parameters?.docs?.source}}},D.parameters={...D.parameters,docs:{...D.parameters?.docs,source:{originalSource:`{
  name: 'AppSlotGrid — 0/3 empty',
  render: () => <AppSlotGrid apps={[]} canRegister />
}`,...D.parameters?.docs?.source}}},O.parameters={...O.parameters,docs:{...O.parameters?.docs,source:{originalSource:`{
  name: 'AppSlotGrid — 1/3 used',
  render: () => <AppSlotGrid apps={nApps(1, 'demo-device')} canRegister />
}`,...O.parameters?.docs?.source}}},k.parameters={...k.parameters,docs:{...k.parameters?.docs,source:{originalSource:`{
  name: 'AppSlotGrid — 3/3 full',
  render: () => <AppSlotGrid apps={nApps(3, 'demo-device')} canRegister={false} />
}`,...k.parameters?.docs?.source}}},A.parameters={...A.parameters,docs:{...A.parameters?.docs,source:{originalSource:`{
  name: 'RenewalQueue — operation states',
  render: () => <RenewalQueueList items={fixtures.renewals} apps={fixtures.apps} apiStatus={liveStatus} />
}`,...A.parameters?.docs?.source}}},j.parameters={...j.parameters,docs:{...j.parameters?.docs,source:{originalSource:`{
  name: 'RenewalQueue — blocked',
  render: () => <RenewalQueueList items={fixtures.renewals.filter(item => item.risk === 'blocked')} apps={fixtures.apps} apiStatus={liveStatus} />
}`,...j.parameters?.docs?.source}}},M.parameters={...M.parameters,docs:{...M.parameters?.docs,source:{originalSource:`{
  name: 'RenewalQueue — empty',
  render: () => <RenewalQueueList items={[]} apps={fixtures.apps} apiStatus={liveStatus} />
}`,...M.parameters?.docs?.source}}},N.parameters={...N.parameters,docs:{...N.parameters?.docs,source:{originalSource:`{
  name: 'Diagnostics — all categories',
  render: () => <DiagnosticIssueList issues={fixtures.issues} />
}`,...N.parameters?.docs?.source}}},P.parameters={...P.parameters,docs:{...P.parameters?.docs,source:{originalSource:`{
  name: 'Diagnostics — install failed',
  render: () => <DiagnosticIssueList issues={fixtures.issues.filter(issue => issue.category.includes('Install'))} />
}`,...P.parameters?.docs?.source}}},F.parameters={...F.parameters,docs:{...F.parameters?.docs,source:{originalSource:`{
  name: 'Diagnostics — resolved',
  render: () => <DiagnosticIssueList issues={fixtures.issues.filter(issue => issue.status === 'resolved')} />
}`,...F.parameters?.docs?.source}}},z.parameters={...z.parameters,docs:{...z.parameters?.docs,source:{originalSource:`{
  name: 'StatusPill — all states',
  render: () => <div className="story-row">{healthStates.map(state => <StatusPill key={state} state={state} label={state} />)}</div>
}`,...z.parameters?.docs?.source}}},B.parameters={...B.parameters,docs:{...B.parameters?.docs,source:{originalSource:`{
  name: 'SourcePill — all sources',
  render: () => <div className="story-row">{sources.map(source => <SourcePill key={source} source={source} label={source} />)}</div>
}`,...B.parameters?.docs?.source}}},V.parameters={...V.parameters,docs:{...V.parameters?.docs,source:{originalSource:`{
  name: 'RoleBadge — all roles',
  render: () => <div className="story-row">{roles.map(role => <RoleBadge key={role} role={role} />)}</div>
}`,...V.parameters?.docs?.source}}},H=[`DeviceCardWifi`,`DeviceCardUsb`,`DeviceCardOffline`,`DeviceCardBlocked`,`DeviceCardFullSlots`,`SlotsEmpty`,`SlotsPartial`,`SlotsFull`,`RenewalRunningQueued`,`RenewalBlocked`,`RenewalEmpty`,`IssuesAll`,`IssueInstallFailed`,`IssueResolved`,`StatusPills`,`SourcePills`,`RoleBadges`]}))();export{T as DeviceCardBlocked,E as DeviceCardFullSlots,w as DeviceCardOffline,C as DeviceCardUsb,S as DeviceCardWifi,P as IssueInstallFailed,F as IssueResolved,N as IssuesAll,j as RenewalBlocked,M as RenewalEmpty,A as RenewalRunningQueued,V as RoleBadges,D as SlotsEmpty,k as SlotsFull,O as SlotsPartial,B as SourcePills,z as StatusPills,H as __namedExportsOrder,p as default};
import{i as e}from"./preload-helper-xPQekRTU.js";import{_ as t,g as n,o as r,u as i}from"./App-WKV86STZ.js";import{i as a,n as o,r as s,t as c}from"./sideportFixtures-BYPsktB3.js";var l,u,d,f,p,m,h,g,_,v,y,b,x,S,C,w,T,E,D,O,k,A,j,M;e((()=>{i(),a(),n(),l={title:`Sideport/Admin Shell`,component:r,parameters:{docs:{description:{component:`Storybook renders the GitHub Pages demo portal with fixture data. The production bundle uses the live .NET API and keeps demo fixtures out of runtime imports.`}}}},u={mode:`demo`,baseUrl:`storybook://demo-data`,message:`Demo data for GitHub Pages and design review.`,canMutate:!1},d={mode:`unavailable`,baseUrl:`/sideport-api`,message:`No Sideport API is reachable. Runtime pages stay empty until the .NET backend responds.`,canMutate:!1},f={mode:`partial`,baseUrl:`/`,message:`Protected API calls are returning 401. Save the browser session token in Settings.`,canMutate:!0},p=(e,t)=>({name:t,args:{data:s,apiStatus:u,initialRoute:e}}),m=p(`overview`,`Overview - healthy mixed fleet`),h=p(`onboarding`,`Onboarding - first run checklist`),g=p(`devices`,`Devices - table and mobile cards`),_=p(`device-detail`,`Device detail - two app slots`),v=p(`catalog`,`App catalog - Cert Clock seed`),y=p(`install-app`,`Install wizard shell - save registration`),b=p(`renewals`,`Renewals - operation history`),x=p(`apple-access`,`Apple Access - read-only probe`),S=p(`diagnostics`,`Diagnostics - filters + trace linked issues`),C=p(`teams`,`Teams - Apple teams and workspace`),w=p(`users`,`Users - roles, members, invite, audit`),T=p(`settings`,`Settings - full control-plane sections`),E={args:{data:o,apiStatus:u,initialRoute:`devices`}},D={args:{data:c,apiStatus:u,initialRoute:`overview`}},O={args:{data:t,apiStatus:d,initialRoute:`onboarding`}},k={args:{data:s,apiStatus:f,initialRoute:`settings`}},A={name:`Command menu - ⌘K search`,args:{data:s,apiStatus:u,initialRoute:`overview`,initialCommandOpen:!0}},j={name:`Device detail - tabs + working refresh`,args:{data:s,apiStatus:u,initialRoute:`device-detail`}},m.parameters={...m.parameters,docs:{...m.parameters?.docs,source:{originalSource:`routeStory('overview', 'Overview - healthy mixed fleet')`,...m.parameters?.docs?.source}}},h.parameters={...h.parameters,docs:{...h.parameters?.docs,source:{originalSource:`routeStory('onboarding', 'Onboarding - first run checklist')`,...h.parameters?.docs?.source}}},g.parameters={...g.parameters,docs:{...g.parameters?.docs,source:{originalSource:`routeStory('devices', 'Devices - table and mobile cards')`,...g.parameters?.docs?.source}}},_.parameters={..._.parameters,docs:{..._.parameters?.docs,source:{originalSource:`routeStory('device-detail', 'Device detail - two app slots')`,..._.parameters?.docs?.source}}},v.parameters={...v.parameters,docs:{...v.parameters?.docs,source:{originalSource:`routeStory('catalog', 'App catalog - Cert Clock seed')`,...v.parameters?.docs?.source}}},y.parameters={...y.parameters,docs:{...y.parameters?.docs,source:{originalSource:`routeStory('install-app', 'Install wizard shell - save registration')`,...y.parameters?.docs?.source}}},b.parameters={...b.parameters,docs:{...b.parameters?.docs,source:{originalSource:`routeStory('renewals', 'Renewals - operation history')`,...b.parameters?.docs?.source}}},x.parameters={...x.parameters,docs:{...x.parameters?.docs,source:{originalSource:`routeStory('apple-access', 'Apple Access - read-only probe')`,...x.parameters?.docs?.source}}},S.parameters={...S.parameters,docs:{...S.parameters?.docs,source:{originalSource:`routeStory('diagnostics', 'Diagnostics - filters + trace linked issues')`,...S.parameters?.docs?.source}}},C.parameters={...C.parameters,docs:{...C.parameters?.docs,source:{originalSource:`routeStory('teams', 'Teams - Apple teams and workspace')`,...C.parameters?.docs?.source}}},w.parameters={...w.parameters,docs:{...w.parameters?.docs,source:{originalSource:`routeStory('users', 'Users - roles, members, invite, audit')`,...w.parameters?.docs?.source}}},T.parameters={...T.parameters,docs:{...T.parameters?.docs,source:{originalSource:`routeStory('settings', 'Settings - full control-plane sections')`,...T.parameters?.docs?.source}}},E.parameters={...E.parameters,docs:{...E.parameters?.docs,source:{originalSource:`{
  args: {
    data: emptyFixtures,
    apiStatus: demoStatus,
    initialRoute: 'devices'
  }
}`,...E.parameters?.docs?.source}}},D.parameters={...D.parameters,docs:{...D.parameters?.docs,source:{originalSource:`{
  args: {
    data: blockedFixtures,
    apiStatus: demoStatus,
    initialRoute: 'overview'
  }
}`,...D.parameters?.docs?.source}}},O.parameters={...O.parameters,docs:{...O.parameters?.docs,source:{originalSource:`{
  args: {
    data: runtimeEmptyData,
    apiStatus: apiUnavailableStatus,
    initialRoute: 'onboarding'
  }
}`,...O.parameters?.docs?.source}}},k.parameters={...k.parameters,docs:{...k.parameters?.docs,source:{originalSource:`{
  args: {
    data: fixtures,
    apiStatus: tokenRequiredStatus,
    initialRoute: 'settings'
  }
}`,...k.parameters?.docs?.source}}},A.parameters={...A.parameters,docs:{...A.parameters?.docs,source:{originalSource:`{
  name: 'Command menu - ⌘K search',
  args: {
    data: fixtures,
    apiStatus: demoStatus,
    initialRoute: 'overview',
    initialCommandOpen: true
  }
}`,...A.parameters?.docs?.source}}},j.parameters={...j.parameters,docs:{...j.parameters?.docs,source:{originalSource:`{
  name: 'Device detail - tabs + working refresh',
  args: {
    data: fixtures,
    apiStatus: demoStatus,
    initialRoute: 'device-detail'
  }
}`,...j.parameters?.docs?.source}}},M=[`OverviewHealthy`,`FirstRunOnboarding`,`DeviceInventory`,`DeviceDetailTwoApps`,`AppCatalogSeed`,`InstallWizardShell`,`RenewalsSingleFlight`,`AppleAccessProbe`,`DiagnosticsTraceLinked`,`TeamsView`,`UsersRoles`,`SettingsSessionAccess`,`EmptyFleet`,`AnisetteBlocked`,`ApiUnavailableRuntime`,`TokenRequiredSettings`,`CommandMenuOpen`,`DeviceDetailTabbed`]}))();export{D as AnisetteBlocked,O as ApiUnavailableRuntime,v as AppCatalogSeed,x as AppleAccessProbe,A as CommandMenuOpen,j as DeviceDetailTabbed,_ as DeviceDetailTwoApps,g as DeviceInventory,S as DiagnosticsTraceLinked,E as EmptyFleet,h as FirstRunOnboarding,y as InstallWizardShell,m as OverviewHealthy,b as RenewalsSingleFlight,T as SettingsSessionAccess,C as TeamsView,k as TokenRequiredSettings,w as UsersRoles,M as __namedExportsOrder,l as default};
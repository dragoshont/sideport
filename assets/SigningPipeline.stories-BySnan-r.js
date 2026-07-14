import{i as e}from"./preload-helper-xPQekRTU.js";import{c as t}from"./iframe-Q-TNvOPE.js";import{s as n,u as r}from"./App-pZMLD6xW.js";var i,a,o,s,c,l,u,d,f,p;e((()=>{r(),i=t(),a={title:`Sideport/Signing Pipeline`,component:n,parameters:{docs:{description:{component:`The sign → install → verify operation view. This is the progress-stage surface the design spec asks for, replacing a bare spinner. It is reused by the install wizard (preview) and the Renewals single-flight strip (live operation).`}}},decorators:[e=>(0,i.jsx)(`div`,{className:`story-pad`,children:(0,i.jsx)(e,{})})]},o=[{id:`authorize`,label:`Authorize`,detail:`GrandSlam login`,state:`done`},{id:`provision`,label:`Provision`,detail:`App ID + profile`,state:`done`},{id:`sign`,label:`Sign`,detail:`zsign re-sign`,state:`active`},{id:`install`,label:`Install`,detail:`Push to device`,state:`pending`},{id:`verify`,label:`Verify`,detail:`Bundle + profile evidence`,state:`pending`}],s=[{id:`authorize`,label:`Authorize`,detail:`GrandSlam login`,state:`done`},{id:`provision`,label:`Provision`,detail:`App ID + profile`,state:`done`},{id:`sign`,label:`Sign`,detail:`zsign re-sign`,state:`done`},{id:`install`,label:`Install`,detail:`Device unreachable`,state:`failed`},{id:`verify`,label:`Verify`,detail:`Bundle + profile evidence`,state:`pending`}],c=[{id:`authorize`,label:`Authorize`,detail:`GrandSlam login`,state:`done`},{id:`provision`,label:`Provision`,detail:`App ID + profile`,state:`done`},{id:`sign`,label:`Sign`,detail:`zsign re-sign`,state:`done`},{id:`install`,label:`Install`,detail:`Push to device`,state:`done`},{id:`verify`,label:`Verify`,detail:`Device evidence matched`,state:`done`}],l={name:`Running — signing in progress`,args:{title:`Refreshing Cert Clock`,stages:o}},u={name:`Failed — install step failed`,args:{title:`Refresh failed — Dice Roll`,stages:s,note:`Install failed: the device became unreachable mid-install. Reconnect the iPhone and retry the operation.`}},d={name:`Complete — signed and verified`,args:{title:`Cert Clock signed and verified`,stages:c}},f={name:`Not started — wizard preview`,args:{title:`Operation preview`,note:`Saving a registration records intent only. These stages run when the refresh operation is wired to this device.`}},l.parameters={...l.parameters,docs:{...l.parameters?.docs,source:{originalSource:`{
  name: 'Running — signing in progress',
  args: {
    title: 'Refreshing Cert Clock',
    stages: running
  }
}`,...l.parameters?.docs?.source}}},u.parameters={...u.parameters,docs:{...u.parameters?.docs,source:{originalSource:`{
  name: 'Failed — install step failed',
  args: {
    title: 'Refresh failed — Dice Roll',
    stages: failed,
    note: 'Install failed: the device became unreachable mid-install. Reconnect the iPhone and retry the operation.'
  }
}`,...u.parameters?.docs?.source}}},d.parameters={...d.parameters,docs:{...d.parameters?.docs,source:{originalSource:`{
  name: 'Complete — signed and verified',
  args: {
    title: 'Cert Clock signed and verified',
    stages: complete
  }
}`,...d.parameters?.docs?.source}}},f.parameters={...f.parameters,docs:{...f.parameters?.docs,source:{originalSource:`{
  name: 'Not started — wizard preview',
  args: {
    title: 'Operation preview',
    note: 'Saving a registration records intent only. These stages run when the refresh operation is wired to this device.'
  }
}`,...f.parameters?.docs?.source}}},p=[`Running`,`Failed`,`Complete`,`NotStarted`]}))();export{d as Complete,u as Failed,f as NotStarted,l as Running,p as __namedExportsOrder,a as default};
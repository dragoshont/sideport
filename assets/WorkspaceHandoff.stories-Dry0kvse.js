import{i as e,s as t}from"./preload-helper-xPQekRTU.js";import{c as n,l as r}from"./iframe-Q-TNvOPE.js";import{Et as i,J as a,Q as o,St as s,c,et as l,t as u,w as d}from"./lucide-react-DqF6b3Ks.js";import{t as f}from"./CanonicalSideport-zq54A334.js";import{a as p,i as m,n as h,r as g,t as _}from"./passkeys-Cd5-9BVF.js";function v(e,t){let n=e===`owner-claim`?`/api/workspace/owner-claims`:`/api/workspace/invitations`;return t===`handoff`?`${n}/handoff`:t===`session`?`${n}/handoff/session`:t===`accept`?`${n}/accept`:t===`enrollment`?`${n}/enrollment`:`${n}/native-passkey/${t===`native-options`?`options`:`complete`}`}function y(e){return`ui-${e}-${typeof crypto<`u`&&`randomUUID`in crypto?crypto.randomUUID():`${Date.now()}-${Math.random()}`}`}async function b(e,t){let n=await fetch(e,{credentials:`same-origin`,cache:`no-store`,...t}),r=await n.text(),i;try{i=r?JSON.parse(r):null}catch{i=null}return{response:n,body:i}}function x(e,t){return typeof e?.message==`string`?e.message:typeof e?.error==`string`?e.error:t}function S(e,t,n=``){return b(e,{method:`POST`,headers:{"Content-Type":`application/json`,...n?{"X-Sideport-CSRF":n}:{}},body:JSON.stringify(t)})}function C({kind:e}){let[t,n]=(0,w.useState)(`exchanging`),[r,u]=(0,w.useState)(null),[f,g]=(0,w.useState)(``),[C,E]=(0,w.useState)(`Checking this private link…`),[D,O]=(0,w.useState)({}),[k,A]=(0,w.useState)(``),[j,M]=(0,w.useState)(``),[N,P]=(0,w.useState)(``),[F,I]=(0,w.useState)(!1),L=(0,w.useRef)(!1),R=e===`owner-claim`,z=D.mode===`passkey`&&D.nativePasskeyEnabled===!0,B=(0,w.useCallback)(async t=>{g((t??await b(`/api/me`)).response.headers.get(`X-Sideport-CSRF`)??``);let r=await b(v(e,`handoff`));if(!r.response.ok){E(x(r.body,`This private handoff is unavailable.`)),n(`error`);return}u(r.body??{}),n(`preview`)},[e]);(0,w.useEffect)(()=>{if(L.current)return;L.current=!0,t();async function t(){let t=await b(`/api/authentication/options`),r=t.body??{};t.response.ok&&O(r);let i=window.location.hash.slice(1);if(window.history.replaceState(window.history.state,``,window.location.pathname),i){let t=R?`claimToken`:`invitationToken`,r=await S(v(e,`handoff`),{[t]:i});if(!r.response.ok){E(x(r.body,`This private link is unavailable.`)),n(`error`);return}}let a;if(R&&r.mode===`passkey`){let e=await b(`/api/workspace/owner-claims/native-passkey/status`);if(!e.response.ok){E(`Sideport could not check whether Owner setup is available. Refresh and try again.`),n(`error`);return}a=(e.body??{}).state,a===`available`&&I(!0)}let o=await b(`/api/me`),s=o.response.ok&&o.body?.authenticated===!0&&(o.body?.via===`oidc`||o.body?.via===`passkey`),c=!R||r.mode!==`passkey`||a!==`available`,l=!1;if(c&&(l=(await b(v(e,`session`))).response.ok,!l)){R&&a===`claimed`?(E(`Owner setup is already complete. Sign in from Sideport Home.`),n(`claimed`)):(E(R?`Open the complete private Owner recovery link that was created for you.`:`Open the complete private invitation link that was sent to you.`),n(`error`));return}if(s){l?await B(o):n(`sign-in`);return}if(r.mode===`oidc`&&r.enrollmentEnabled===!0){let t=await S(v(e,`enrollment`),{idempotencyKey:y(e)});t.response.ok&&typeof t.body?.enrollmentUrl==`string`&&A(t.body.enrollmentUrl)}n(`sign-in`),E(r.mode===`passkey`?`Create a passkey using this device’s built-in security.`:`Sign in through ${r.providerLabel??`your identity provider`} to continue.`)}},[R,e,B]);async function V(){n(`creating`);try{let t=R?{displayName:j.trim(),email:N.trim()}:{},r=await S(v(e,`native-options`),t);if(!r.response.ok||typeof r.body?.creationOptions!=`string`){E(x(r.body,`Sideport could not start passkey creation.`)),n(`error`);return}let i=await navigator.credentials.create(_(r.body.creationOptions));if(!i)throw Error(`Passkey creation did not finish.`);let a=await S(v(e,`native-complete`),{...t,credentialJson:h(i),idempotencyKey:y(e)});if(!a.response.ok){E(x(a.body,`Sideport could not save this passkey.`)),n(`error`);return}W()}catch(e){E(m(e,`create`)),n(`sign-in`)}}async function H(){n(`creating`);try{let e=await S(`/api/authentication/native-passkey/options`,{});if(!e.response.ok||typeof e.body?.requestOptions!=`string`){E(x(e.body,`Sideport could not start passkey sign-in.`)),n(`error`);return}let t=await navigator.credentials.get(p(e.body.requestOptions));if(!t)throw Error(`Passkey sign-in did not finish.`);let r=await S(`/api/authentication/native-passkey/complete`,{credentialJson:h(t),idempotencyKey:y(`login`)});if(!r.response.ok){E(x(r.body,`Sideport could not verify this passkey.`)),n(`error`);return}await B()}catch(e){E(m(e,`sign-in`)),n(`sign-in`)}}async function U(){n(`accepting`);let t=await S(v(e,`accept`),{idempotencyKey:y(e)},f);if(!t.response.ok){E(x(t.body,`Sideport could not finish this handoff.`)),n(`error`);return}W()}function W(){n(`done`),window.setTimeout(()=>window.location.assign(`/`),500)}let G=r?.account,K=r?.claim?.kind===`recovery`,q=R?K?`Recover Sideport owner access`:`Finish setting up Sideport`:`Join Sideport`,J=j.trim().length>0&&N.trim().length>2;return(0,T.jsxs)(`div`,{className:`spc-invitation`,"data-testid":`runtime-${e}`,children:[(0,T.jsxs)(`header`,{children:[(0,T.jsx)(`strong`,{children:`Sideport`}),(0,T.jsx)(`span`,{className:`spc-eyebrow`,children:F?`First setup`:`Private link`})]}),(0,T.jsxs)(`main`,{children:[(0,T.jsx)(`div`,{className:`spc-invite-illustration`,children:R?(0,T.jsx)(d,{"aria-hidden":`true`,size:44}):(0,T.jsx)(c,{"aria-hidden":`true`,size:44})}),(0,T.jsx)(`span`,{className:`spc-eyebrow`,children:R?`Owner access`:`Trusted access`}),(0,T.jsx)(`h1`,{children:q}),t===`exchanging`||t===`creating`||t===`accepting`?(0,T.jsxs)(`div`,{role:`status`,children:[(0,T.jsx)(a,{"aria-hidden":`true`,className:`spin`,size:22}),` `,t===`accepting`?`Saving access…`:t===`creating`?`Waiting for this device…`:C]}):null,t===`sign-in`&&z?(0,T.jsxs)(T.Fragment,{children:[(0,T.jsxs)(`p`,{className:`spc-lead`,children:[C,` Use Face ID, Touch ID, Windows Hello, or your password manager. Nothing extra to remember.`]}),R?(0,T.jsxs)(`div`,{className:`spc-identity-form`,children:[(0,T.jsxs)(`label`,{children:[(0,T.jsx)(`span`,{children:`Name`}),(0,T.jsx)(`input`,{autoComplete:`name`,onChange:e=>M(e.currentTarget.value),placeholder:`Your name`,value:j})]}),(0,T.jsxs)(`label`,{children:[(0,T.jsx)(`span`,{children:`Email`}),(0,T.jsx)(`input`,{autoComplete:`email`,inputMode:`email`,onChange:e=>P(e.currentTarget.value),placeholder:`you@example.com`,type:`email`,value:N})]})]}):null,(0,T.jsxs)(`button`,{className:`spc-button primary large`,disabled:R&&!J,onClick:()=>void V(),type:`button`,children:[(0,T.jsx)(o,{"aria-hidden":`true`,size:18}),` Create passkey`]}),(0,T.jsx)(`button`,{className:`spc-text-button`,onClick:()=>void H(),type:`button`,children:`Use an existing passkey`})]}):null,t===`sign-in`&&!z?(0,T.jsxs)(T.Fragment,{children:[(0,T.jsxs)(`p`,{className:`spc-lead`,children:[C,` The private token has already been removed from the address bar.`]}),k?(0,T.jsx)(`button`,{className:`spc-button primary large`,onClick:()=>window.location.assign(k),type:`button`,children:D.enrollmentLabel||`Create passkey`}):null,(0,T.jsx)(`button`,{className:k?`spc-button secondary large`:`spc-button primary large`,onClick:()=>window.location.assign(`/login?returnUrl=${encodeURIComponent(window.location.pathname)}`),type:`button`,children:D.loginLabel||`Continue with ${D.providerLabel||`your account`}`})]}):null,t===`preview`?(0,T.jsxs)(T.Fragment,{children:[(0,T.jsx)(`p`,{className:`spc-lead`,children:`Confirm the signed-in account before Sideport changes access.`}),(0,T.jsxs)(`div`,{className:`spc-passkey-card`,children:[(0,T.jsx)(s,{"aria-hidden":`true`,size:24}),(0,T.jsxs)(`div`,{children:[(0,T.jsx)(`strong`,{children:G?.displayName||`Signed-in account`}),(0,T.jsx)(`span`,{children:G?.email||`Authenticated through ${D.providerLabel||`your identity provider`}`})]})]}),(0,T.jsxs)(`div`,{className:`spc-passkey-card`,children:[(0,T.jsx)(d,{"aria-hidden":`true`,size:24}),(0,T.jsxs)(`div`,{children:[(0,T.jsx)(`strong`,{children:R?`Owner access`:`Member access`}),(0,T.jsx)(`span`,{children:R?`Manage people, Apple signing, apps, iPhones, and settings.`:(r?.invitation?.permissions??[`Choose approved apps`,`Use your own iPhone`,`Receive home Wi-Fi refreshes`]).join(` · `)})]})]}),(0,T.jsx)(`button`,{className:`spc-button primary large`,onClick:()=>void U(),type:`button`,children:R?K?`Recover owner access`:`Finish owner setup`:`Join Sideport`})]}):null,t===`done`?(0,T.jsxs)(`div`,{className:`spc-invite-result`,role:`status`,children:[(0,T.jsx)(i,{"aria-hidden":`true`,size:20}),(0,T.jsxs)(`div`,{children:[(0,T.jsx)(`strong`,{children:`Access saved`}),(0,T.jsx)(`span`,{children:`Opening Sideport…`})]})]}):null,t===`claimed`?(0,T.jsxs)(T.Fragment,{children:[(0,T.jsxs)(`div`,{className:`spc-invite-result`,role:`status`,children:[(0,T.jsx)(i,{"aria-hidden":`true`,size:20}),(0,T.jsxs)(`div`,{children:[(0,T.jsx)(`strong`,{children:`Sideport is already set up`}),(0,T.jsx)(`span`,{children:C})]})]}),(0,T.jsxs)(`button`,{className:`spc-button primary large`,onClick:()=>window.location.assign(`/login?returnUrl=%2F`),type:`button`,children:[(0,T.jsx)(o,{"aria-hidden":`true`,size:18}),` Sign in`]})]}):null,t===`error`?(0,T.jsxs)(`div`,{className:`spc-inline-note warning`,role:`alert`,children:[(0,T.jsx)(l,{"aria-hidden":`true`,size:18}),(0,T.jsxs)(`div`,{children:[(0,T.jsx)(`strong`,{children:`This link cannot continue.`}),(0,T.jsx)(`span`,{children:C})]})]}):null,(0,T.jsx)(`p`,{className:`spc-fine-print`,children:`Your passkey stays on your trusted device. Sideport never receives your Apple password.`})]})]})}var w,T,E=e((()=>{w=t(r(),1),u(),g(),f(),T=n(),C.__docgenInfo={description:``,methods:[],displayName:`WorkspaceHandoff`,props:{kind:{required:!0,tsType:{name:`union`,raw:`'owner-claim' | 'invitation'`,elements:[{name:`literal`,value:`'owner-claim'`},{name:`literal`,value:`'invitation'`}]},description:``}}}}));function D(e){return typeof e==`string`?e:e instanceof URL?e.pathname:new URL(e.url).pathname}function O(e,t=!0){let n=window.fetch;return window.fetch=(async n=>{let r=D(n);return r===`/api/authentication/options`?Response.json({mode:`oidc`,oidcEnabled:!0,nativePasskeyEnabled:!1,provider:`company-sso`,providerLabel:`Company account`,loginLabel:`Continue with Company SSO`,enrollmentLabel:`Create passkey`,preferredMethod:`passkey`,enrollmentEnabled:e}):r===`/api/me`?Response.json({authenticated:!1,via:`none`},{status:401}):r.endsWith(`/handoff/session`)?t?Response.json({available:!0}):Response.json({error:`owner-claim-unavailable`},{status:404}):r.endsWith(`/enrollment`)?Response.json({available:!0,enrollmentUrl:`https://identity.example/enroll/fixture`}):Response.json({error:`unexpected-request`,message:`Unexpected Storybook request: ${r}`},{status:500})}),()=>{window.fetch=n}}function k(e,t=`available`,n=!0){let r=window.fetch,i=Object.getOwnPropertyDescriptor(navigator,`credentials`);return Object.defineProperty(navigator,"credentials",{configurable:!0,value:{create:M(async()=>({toJSON:()=>({id:`fixture-passkey`,rawId:`AQIDBA`,type:`public-key`,response:{clientDataJSON:`AQID`,attestationObject:`BAUG`}})})),get:M()}}),window.fetch=(async r=>{let i=D(r);return i===`/api/authentication/options`?Response.json({mode:`passkey`,oidcEnabled:!1,nativePasskeyEnabled:!0,providerLabel:`Sideport passkey`,enrollmentLabel:`Create passkey`,enrollmentEnabled:!0}):i===`/api/me`?Response.json({authenticated:!1,via:`none`},{status:401}):i===`/api/workspace/owner-claims/native-passkey/status`?Response.json({mode:`passkey`,state:t}):i.endsWith(`/handoff/session`)?!n||t!==`available`&&e===`owner-claim`?Response.json({error:`owner-claim-unavailable`},{status:404}):Response.json({available:!0}):i===`/api/workspace/${e===`owner-claim`?`owner-claims`:`invitations`}/native-passkey/options`?Response.json({mode:`passkey`,creationOptions:I}):i.endsWith(`/native-passkey/complete`)?Response.json({signedIn:!0,method:`passkey`,acceptance:{replayed:!1}}):Response.json({error:`unexpected-request`,message:`Unexpected Storybook request: ${i}`},{status:500})}),()=>{window.fetch=r,i?Object.defineProperty(navigator,"credentials",i):Reflect.deleteProperty(navigator,`credentials`)}}function A(){let e=window.fetch;return window.fetch=(async e=>{let t=D(e);return t===`/api/authentication/options`?Response.json({mode:`passkey`,nativePasskeyEnabled:!0,enrollmentEnabled:!0}):t===`/api/me`?Response.json({authenticated:!1,via:`none`},{status:401}):t===`/api/workspace/owner-claims/native-passkey/status`?Response.json({mode:`passkey`,state:`available`}):Response.json({error:`unexpected-request`,message:`Unexpected Storybook request: ${t}`},{status:500})}),()=>{window.fetch=e}}var j,M,N,P,F,I,L,R,z,B,V,H,U,W,G,K,q,J,Y;e((()=>{E(),{expect:j,fn:M,userEvent:N,waitFor:P,within:F}=__STORYBOOK_MODULE_TEST__,I=JSON.stringify({challenge:`AQIDBA`,rp:{id:`localhost`,name:`Sideport`},user:{id:`BQYHCA`,name:`sideport-fixture`,displayName:`Home Owner`},pubKeyCredParams:[{type:`public-key`,alg:-7}],authenticatorSelection:{residentKey:`required`,userVerification:`required`}}),L={title:`Sideport/Workspace Handoff`,component:C,args:{kind:`invitation`},parameters:{layout:`fullscreen`}},R={args:{kind:`owner-claim`},beforeEach:()=>k(`owner-claim`)},z={beforeEach:()=>k(`invitation`)},B={beforeEach:()=>k(`invitation`,`available`,!1),play:async({canvasElement:e})=>{let t=F(e);await j(t.findByRole(`alert`)).resolves.toHaveTextContent(`Open the complete private invitation link`),await j(t.queryByRole(`button`,{name:`Create passkey`})).not.toBeInTheDocument()}},V={args:{kind:`owner-claim`},beforeEach:()=>A(),play:async({canvasElement:e})=>{let t=F(e);await j(t.findByText(`First setup`)).resolves.toBeVisible(),await j(t.findByRole(`textbox`,{name:`Name`})).resolves.toBeVisible(),await j(t.getByRole(`textbox`,{name:`Email`})).toBeVisible(),await j(t.getByRole(`button`,{name:`Create passkey`})).toBeDisabled(),await j(t.queryByText(/setup link|startup logs/i)).not.toBeInTheDocument()}},H={args:{kind:`owner-claim`},beforeEach:()=>k(`owner-claim`,`claimed`),play:async({canvasElement:e})=>{let t=F(e);await j(t.findByText(`Sideport is already set up`)).resolves.toBeVisible(),await j(t.getByRole(`button`,{name:`Sign in`})).toBeVisible(),await j(t.queryByRole(`textbox`,{name:`Name`})).not.toBeInTheDocument(),await j(t.queryByRole(`textbox`,{name:`Email`})).not.toBeInTheDocument()}},U={args:{kind:`owner-claim`},beforeEach:()=>k(`owner-claim`,`private-link-required`),play:async({canvasElement:e})=>{let t=F(e);await j(t.findByRole(`alert`)).resolves.toHaveTextContent(`Open the complete private Owner recovery link`),await j(t.queryByRole(`textbox`,{name:`Name`})).not.toBeInTheDocument(),await j(t.queryByRole(`button`,{name:`Create passkey`})).not.toBeInTheDocument()}},W={args:{kind:`owner-claim`},beforeEach:()=>k(`owner-claim`),play:async({canvasElement:e})=>{let t=F(e),n=await t.findByRole(`button`,{name:`Create passkey`});await j(n).toBeDisabled(),await N.type(t.getByRole(`textbox`,{name:`Name`}),`Home Owner`),await N.type(t.getByRole(`textbox`,{name:`Email`}),`owner@example.test`),await j(n).toBeEnabled(),await N.click(n),await j(t.findByText(`Access saved`)).resolves.toBeVisible(),await j(t.queryByText(/Authentik|Company account/)).not.toBeInTheDocument()}},G={beforeEach:()=>k(`invitation`),play:async({canvasElement:e})=>{let t=F(e);await N.click(await t.findByRole(`button`,{name:`Create passkey`})),await j(t.findByText(`Access saved`)).resolves.toBeVisible(),await j(t.queryByRole(`textbox`)).not.toBeInTheDocument()}},K={beforeEach:()=>O(!0),play:async({canvasElement:e})=>{let t=F(e);await P(()=>j(t.getByRole(`button`,{name:`Create passkey`})).toBeVisible()),await j(t.getByRole(`button`,{name:`Continue with Company SSO`})).toBeVisible(),await j(t.queryByRole(`textbox`,{name:`Name`})).not.toBeInTheDocument()}},q={beforeEach:()=>O(!1),play:async({canvasElement:e})=>{let t=F(e);await P(()=>j(t.getByRole(`button`,{name:`Continue with Company SSO`})).toBeVisible()),await j(t.queryByRole(`button`,{name:`Create passkey`})).not.toBeInTheDocument()}},J={args:{kind:`owner-claim`},beforeEach:()=>O(!0,!1),play:async({canvasElement:e})=>{let t=F(e);await j(t.findByRole(`alert`)).resolves.toHaveTextContent(`Open the complete private Owner recovery link`),await j(t.queryByRole(`button`,{name:`Create passkey`})).not.toBeInTheDocument(),await j(t.queryByRole(`button`,{name:`Continue with Company SSO`})).not.toBeInTheDocument()}},R.parameters={...R.parameters,docs:{...R.parameters?.docs,source:{originalSource:`{
  args: {
    kind: 'owner-claim'
  },
  beforeEach: () => installNativeMock('owner-claim')
}`,...R.parameters?.docs?.source}}},z.parameters={...z.parameters,docs:{...z.parameters?.docs,source:{originalSource:`{
  beforeEach: () => installNativeMock('invitation')
}`,...z.parameters?.docs?.source}}},B.parameters={...B.parameters,docs:{...B.parameters?.docs,source:{originalSource:`{
  beforeEach: () => installNativeMock('invitation', 'available', false),
  play: async ({
    canvasElement
  }) => {
    const canvas = within(canvasElement);
    await expect(canvas.findByRole('alert')).resolves.toHaveTextContent('Open the complete private invitation link');
    await expect(canvas.queryByRole('button', {
      name: 'Create passkey'
    })).not.toBeInTheDocument();
  }
}`,...B.parameters?.docs?.source}}},V.parameters={...V.parameters,docs:{...V.parameters?.docs,source:{originalSource:`{
  args: {
    kind: 'owner-claim'
  },
  beforeEach: () => installDirectOwnerSetupMock(),
  play: async ({
    canvasElement
  }) => {
    const canvas = within(canvasElement);
    await expect(canvas.findByText('First setup')).resolves.toBeVisible();
    await expect(canvas.findByRole('textbox', {
      name: 'Name'
    })).resolves.toBeVisible();
    await expect(canvas.getByRole('textbox', {
      name: 'Email'
    })).toBeVisible();
    await expect(canvas.getByRole('button', {
      name: 'Create passkey'
    })).toBeDisabled();
    await expect(canvas.queryByText(/setup link|startup logs/i)).not.toBeInTheDocument();
  }
}`,...V.parameters?.docs?.source}}},H.parameters={...H.parameters,docs:{...H.parameters?.docs,source:{originalSource:`{
  args: {
    kind: 'owner-claim'
  },
  beforeEach: () => installNativeMock('owner-claim', 'claimed'),
  play: async ({
    canvasElement
  }) => {
    const canvas = within(canvasElement);
    await expect(canvas.findByText('Sideport is already set up')).resolves.toBeVisible();
    await expect(canvas.getByRole('button', {
      name: 'Sign in'
    })).toBeVisible();
    await expect(canvas.queryByRole('textbox', {
      name: 'Name'
    })).not.toBeInTheDocument();
    await expect(canvas.queryByRole('textbox', {
      name: 'Email'
    })).not.toBeInTheDocument();
  }
}`,...H.parameters?.docs?.source}}},U.parameters={...U.parameters,docs:{...U.parameters?.docs,source:{originalSource:`{
  args: {
    kind: 'owner-claim'
  },
  beforeEach: () => installNativeMock('owner-claim', 'private-link-required'),
  play: async ({
    canvasElement
  }) => {
    const canvas = within(canvasElement);
    await expect(canvas.findByRole('alert')).resolves.toHaveTextContent('Open the complete private Owner recovery link');
    await expect(canvas.queryByRole('textbox', {
      name: 'Name'
    })).not.toBeInTheDocument();
    await expect(canvas.queryByRole('button', {
      name: 'Create passkey'
    })).not.toBeInTheDocument();
  }
}`,...U.parameters?.docs?.source}}},W.parameters={...W.parameters,docs:{...W.parameters?.docs,source:{originalSource:`{
  args: {
    kind: 'owner-claim'
  },
  beforeEach: () => installNativeMock('owner-claim'),
  play: async ({
    canvasElement
  }) => {
    const canvas = within(canvasElement);
    const create = await canvas.findByRole('button', {
      name: 'Create passkey'
    });
    await expect(create).toBeDisabled();
    await userEvent.type(canvas.getByRole('textbox', {
      name: 'Name'
    }), 'Home Owner');
    await userEvent.type(canvas.getByRole('textbox', {
      name: 'Email'
    }), 'owner@example.test');
    await expect(create).toBeEnabled();
    await userEvent.click(create);
    await expect(canvas.findByText('Access saved')).resolves.toBeVisible();
    await expect(canvas.queryByText(/Authentik|Company account/)).not.toBeInTheDocument();
  }
}`,...W.parameters?.docs?.source}}},G.parameters={...G.parameters,docs:{...G.parameters?.docs,source:{originalSource:`{
  beforeEach: () => installNativeMock('invitation'),
  play: async ({
    canvasElement
  }) => {
    const canvas = within(canvasElement);
    await userEvent.click(await canvas.findByRole('button', {
      name: 'Create passkey'
    }));
    await expect(canvas.findByText('Access saved')).resolves.toBeVisible();
    await expect(canvas.queryByRole('textbox')).not.toBeInTheDocument();
  }
}`,...G.parameters?.docs?.source}}},K.parameters={...K.parameters,docs:{...K.parameters?.docs,source:{originalSource:`{
  beforeEach: () => installOidcMock(true),
  play: async ({
    canvasElement
  }) => {
    const canvas = within(canvasElement);
    await waitFor(() => expect(canvas.getByRole('button', {
      name: 'Create passkey'
    })).toBeVisible());
    await expect(canvas.getByRole('button', {
      name: 'Continue with Company SSO'
    })).toBeVisible();
    await expect(canvas.queryByRole('textbox', {
      name: 'Name'
    })).not.toBeInTheDocument();
  }
}`,...K.parameters?.docs?.source}}},q.parameters={...q.parameters,docs:{...q.parameters?.docs,source:{originalSource:`{
  beforeEach: () => installOidcMock(false),
  play: async ({
    canvasElement
  }) => {
    const canvas = within(canvasElement);
    await waitFor(() => expect(canvas.getByRole('button', {
      name: 'Continue with Company SSO'
    })).toBeVisible());
    await expect(canvas.queryByRole('button', {
      name: 'Create passkey'
    })).not.toBeInTheDocument();
  }
}`,...q.parameters?.docs?.source}}},J.parameters={...J.parameters,docs:{...J.parameters?.docs,source:{originalSource:`{
  args: {
    kind: 'owner-claim'
  },
  beforeEach: () => installOidcMock(true, false),
  play: async ({
    canvasElement
  }) => {
    const canvas = within(canvasElement);
    await expect(canvas.findByRole('alert')).resolves.toHaveTextContent('Open the complete private Owner recovery link');
    await expect(canvas.queryByRole('button', {
      name: 'Create passkey'
    })).not.toBeInTheDocument();
    await expect(canvas.queryByRole('button', {
      name: 'Continue with Company SSO'
    })).not.toBeInTheDocument();
  }
}`,...J.parameters?.docs?.source}}},Y=[`NativeOwnerReady`,`NativeInvitationReady`,`NativeInvitationRequiresPrivateLink`,`NativeOwnerDirectVisitStartsSetup`,`NativeOwnerAlreadyClaimed`,`NativeOwnerRecoveryRequiresPrivateLink`,`NativeOwnerCreatesPasskey`,`NativeInvitationCreatesPasskey`,`OidcProviderEnrollmentAndExistingAccount`,`OidcExistingAccountOnly`,`OidcOwnerStillRequiresPrivateLink`]}))();export{G as NativeInvitationCreatesPasskey,z as NativeInvitationReady,B as NativeInvitationRequiresPrivateLink,H as NativeOwnerAlreadyClaimed,W as NativeOwnerCreatesPasskey,V as NativeOwnerDirectVisitStartsSetup,R as NativeOwnerReady,U as NativeOwnerRecoveryRequiresPrivateLink,q as OidcExistingAccountOnly,J as OidcOwnerStillRequiresPrivateLink,K as OidcProviderEnrollmentAndExistingAccount,Y as __namedExportsOrder,L as default};
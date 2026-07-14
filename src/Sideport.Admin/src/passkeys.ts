type PublicKeyCredentialConstructor = typeof PublicKeyCredential & {
  parseCreationOptionsFromJSON?: (options: Record<string, unknown>) => PublicKeyCredentialCreationOptions
  parseRequestOptionsFromJSON?: (options: Record<string, unknown>) => PublicKeyCredentialRequestOptions
}

function decodeBase64Url(value: string): ArrayBuffer {
  const normalized = value.replace(/-/g, '+').replace(/_/g, '/')
  const padded = normalized.padEnd(Math.ceil(normalized.length / 4) * 4, '=')
  const binary = window.atob(padded)
  const bytes = Uint8Array.from(binary, (character) => character.charCodeAt(0))
  return bytes.buffer
}

function encodeBase64Url(value: ArrayBuffer | null): string | null {
  if (value === null) return null
  const bytes = new Uint8Array(value)
  let binary = ''
  for (const byte of bytes) binary += String.fromCharCode(byte)
  return window.btoa(binary).replace(/\+/g, '-').replace(/\//g, '_').replace(/=+$/g, '')
}

function record(value: unknown): Record<string, unknown> {
  if (typeof value !== 'object' || value === null || Array.isArray(value))
    throw new Error('Sideport received invalid passkey options.')
  return value as Record<string, unknown>
}

function credentialDescriptors(value: unknown): PublicKeyCredentialDescriptor[] | undefined {
  if (!Array.isArray(value)) return undefined
  return value.map((item) => {
    const descriptor = record(item)
    return {
      ...descriptor,
      id: decodeBase64Url(String(descriptor.id ?? '')),
    } as PublicKeyCredentialDescriptor
  })
}

function optionPayload(json: string): Record<string, unknown> {
  const parsed = record(JSON.parse(json))
  return 'publicKey' in parsed ? record(parsed.publicKey) : parsed
}

export function creationOptionsFromJson(json: string): CredentialCreationOptions {
  const source = optionPayload(json)
  const constructor = window.PublicKeyCredential as PublicKeyCredentialConstructor | undefined
  if (constructor?.parseCreationOptionsFromJSON)
    return { publicKey: constructor.parseCreationOptionsFromJSON(source) }

  const user = record(source.user)
  return {
    publicKey: {
      ...source,
      challenge: decodeBase64Url(String(source.challenge ?? '')),
      user: { ...user, id: decodeBase64Url(String(user.id ?? '')) },
      excludeCredentials: credentialDescriptors(source.excludeCredentials),
    } as PublicKeyCredentialCreationOptions,
  }
}

export function requestOptionsFromJson(json: string): CredentialRequestOptions {
  const source = optionPayload(json)
  const constructor = window.PublicKeyCredential as PublicKeyCredentialConstructor | undefined
  if (constructor?.parseRequestOptionsFromJSON)
    return { publicKey: constructor.parseRequestOptionsFromJSON(source) }

  return {
    publicKey: {
      ...source,
      challenge: decodeBase64Url(String(source.challenge ?? '')),
      allowCredentials: credentialDescriptors(source.allowCredentials),
    } as PublicKeyCredentialRequestOptions,
  }
}

export function credentialJson(credential: Credential): string {
  const publicKey = credential as PublicKeyCredential & { toJSON?: () => unknown }
  if (typeof publicKey.toJSON === 'function') return JSON.stringify(publicKey.toJSON())

  const response = publicKey.response
  const base = {
    id: publicKey.id,
    rawId: encodeBase64Url(publicKey.rawId),
    type: publicKey.type,
    authenticatorAttachment: publicKey.authenticatorAttachment,
    clientExtensionResults: publicKey.getClientExtensionResults(),
  }
  if ('attestationObject' in response) {
    const attestation = response as AuthenticatorAttestationResponse
    return JSON.stringify({
      ...base,
      response: {
        clientDataJSON: encodeBase64Url(attestation.clientDataJSON),
        attestationObject: encodeBase64Url(attestation.attestationObject),
        transports: attestation.getTransports?.() ?? [],
      },
    })
  }

  const assertion = response as AuthenticatorAssertionResponse
  return JSON.stringify({
    ...base,
    response: {
      clientDataJSON: encodeBase64Url(assertion.clientDataJSON),
      authenticatorData: encodeBase64Url(assertion.authenticatorData),
      signature: encodeBase64Url(assertion.signature),
      userHandle: encodeBase64Url(assertion.userHandle),
    },
  })
}

export function passkeyError(error: unknown, action: 'create' | 'sign-in'): string {
  if (error instanceof DOMException && error.name === 'NotAllowedError')
    return action === 'create' ? 'Passkey creation was canceled. Try again when you’re ready.' : 'Passkey sign-in was canceled.'
  if (error instanceof Error && error.message) return error.message
  return action === 'create' ? 'Sideport could not create this passkey.' : 'Sideport could not sign in with this passkey.'
}

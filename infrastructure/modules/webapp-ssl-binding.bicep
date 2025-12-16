@description('Name of the web app to bind')
param webAppName string

@description('Custom hostname to bind (e.g., portal.contoso.com)')
param hostName string

@description('Name of the managed certificate to use')
param certificateName string

resource certificate 'Microsoft.Web/certificates@2023-01-01' existing = {
  name: certificateName
}

resource hostnameBinding 'Microsoft.Web/sites/hostnameBindings@2023-01-01' = {
  name: '${webAppName}/${hostName}'
  properties: {
    hostNameType: 'Verified'
    customHostNameDnsRecordType: 'CName'
    sslState: 'SniEnabled'
    thumbprint: certificate.properties.thumbprint
  }
}

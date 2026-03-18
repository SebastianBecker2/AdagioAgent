<?xml version="1.0" encoding="utf-8"?>
<!--
  Suppress selected files from the harvested ApplicationFiles group.
  - AdagioMachineAgent.exe is defined explicitly in Package.wxs together with
    ServiceInstall / ServiceControl.
  - appsettings.json is defined explicitly in Package.wxs with
    NeverOverwrite="yes" so user edits are preserved across upgrades.
-->
<xsl:stylesheet version="1.0"
    xmlns:xsl="http://www.w3.org/1999/XSL/Transform"
    xmlns:wix="http://wixtoolset.org/schemas/v4/wxs">

  <xsl:output method="xml" encoding="UTF-8" indent="yes" />

  <!-- Identity transform: copy everything unchanged by default. -->
  <xsl:template match="@*|node()">
    <xsl:copy>
      <xsl:apply-templates select="@*|node()" />
    </xsl:copy>
  </xsl:template>

  <!-- Drop Components whose file source is managed explicitly in Package.wxs. -->
  <xsl:template
    match="wix:Component[
      wix:File[contains(@Source, 'AdagioMachineAgent.exe')]
      or wix:File[contains(@Source, 'appsettings.json')]
    ]" />

  <!-- Also drop ComponentRef entries that point to excluded components. -->
  <xsl:template
    match="wix:ComponentRef[@Id = //wix:Component[
      wix:File[contains(@Source, 'AdagioMachineAgent.exe')]
      or wix:File[contains(@Source, 'appsettings.json')]
    ]/@Id]" />

</xsl:stylesheet>

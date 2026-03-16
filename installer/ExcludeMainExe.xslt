<?xml version="1.0" encoding="utf-8"?>
<!--
  Suppress AdagioMachineAgent.exe from the harvested ApplicationFiles group.
  The executable is defined explicitly in Package.wxs (together with
  ServiceInstall / ServiceControl), so it must not appear a second time in
  the harvested group.
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

  <!--
    Drop the Component whose File source ends with "AdagioMachineAgent.exe".
    Using contains() covers absolute and relative source paths on all Windows
    path separators.
  -->
  <xsl:template
    match="wix:Component[wix:File[contains(@Source, 'AdagioMachineAgent.exe')]]" />

</xsl:stylesheet>

# Third-party notices

Honor Control Service uses the following third-party hardware monitoring library:

- LibreHardwareMonitorLib 0.9.6, licensed under MPL-2.0: https://github.com/LibreHardwareMonitor/LibreHardwareMonitor/blob/master/LICENSE
- PawnIO modules distributed by LibreHardwareMonitor, licensed under LGPL-2.1: https://github.com/LibreHardwareMonitor/LibreHardwareMonitor/blob/master/THIRD-PARTY-NOTICES.txt

LibreHardwareMonitor may use its PawnIO driver modules to read hardware sensors. Some readings require the service's LocalSystem privileges. Honor Control does not ask the control panel to elevate for telemetry reads.

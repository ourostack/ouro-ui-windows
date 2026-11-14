# Person check: does person-only need proof of the person?

**Status: open, for Ari to decide. The check is built and off.**

A person-only action (core's `consent: "person-only"`, such as placing an order and paying) is refused at every agent door: the MCP server answers core's typed refusal, and the App Actions door never registers it and refuses a launch. Only the person's own press runs it.

The gap: on Windows, a press is anything that invokes the button, and UI Automation can invoke it too. So a program driving the screen can press Place order the way an assistive technology would. The runtime can't tell a person's press from an automation's press, and it shouldn't try: blocking UI Automation would break screen readers and voice control.

## What is built

`OuroActionRuntime.PersonCheck` runs before any person-only action at the person's door. It is `null`, so no check runs, unless the app sets it. `OuroPerson.WindowsHello(() => hwnd)` asks Windows Hello (face, fingerprint or PIN) in a prompt owned by the app's window. When the person isn't verified, the call answers `person-not-verified` and nothing runs. `tests/Ouro.Tests/RuntimeTests.cs` covers off by default, refused when not verified, and other actions unaffected.

## The choice

| | Off (today) | On for person-only |
| --- | --- | --- |
| What a person feels | One press, as on the web | One press, then Windows Hello, for each person-only action |
| What automation can do | Press person-only buttons through UI Automation | Press them, but Windows Hello stops it unless the person is there |
| Where it can't run | n/a | PCs without Windows Hello set up: the action refuses with how to set it up |

Apple faces the same question with LocalAuthentication. Whatever Ari decides should hold on both kits, and in core's wording of person-only.

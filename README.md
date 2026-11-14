# Ouro UI for Windows

Ouro UI's native kit for Windows, and the agent doors (App Actions and an MCP server for the On-device Agent Registry) generated from [ouro-ui-core](https://github.com/ourostack/ouro-ui-core)'s one action declaration.

**Status: spike.** Nothing here is released. `spikes/` holds the measured experiments behind the native-strategy decision record in [ourostack/ouro-ui](https://github.com/ourostack/ouro-ui/tree/main/docs/decisions).

## How the kits fit together

- **Web** (`ourostack/ouro-ui`) is the build-once layer that runs everywhere.
- **Native** is one kit per platform in that platform's own toolkit: SwiftUI on Apple (`ourostack/ouro-ui-apple`), this kit on Windows, Compose on Android.
- The kits share **only the core**: tokens, `defineAction` with its consent classes, the claims and the A2UI catalog. They never share rendering.

## License

MIT

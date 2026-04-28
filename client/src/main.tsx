import { StrictMode } from 'react'
import { createRoot } from 'react-dom/client'
import './index.css'
import App from './App.tsx'
import { SpriteProbe } from './dev/SpriteProbe'

// Dev-only routing: ?probe=sprites swaps in the sprite-coord probe
// instead of the normal app shell. Lets us identify sheet coordinates
// for new sprite declarations without dragging the asset preload +
// identity + lobby flow into a debug session.
const probe = new URLSearchParams(window.location.search).get('probe')
const root = createRoot(document.getElementById('root')!)
if (probe === 'sprites') {
  root.render(<SpriteProbe />)
} else {
  root.render(
    <StrictMode>
      <App />
    </StrictMode>,
  )
}

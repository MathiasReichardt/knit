import { z } from 'zod'

// The host's CSP has no 'unsafe-eval': skip Zod's `new Function` probe, which a
// browser reports as a violation even though Zod catches the throw.
z.config({ jitless: true })

import { Link, Outlet, createRootRoute } from '@tanstack/react-router'
import { HamburgerMenu } from '../components/HamburgerMenu'
import { RepoIndicator } from '../components/RepoIndicator'
import { RepoNameTitle } from '../components/RepoNameTitle'
import { DoctorChip } from '../components/DoctorChip'
import { IdentityChip } from '../components/IdentityChip'
import { NavRail } from '../components/NavRail'
import { SearchBox } from '../components/SearchBox'
import { ShortcutsOverlay } from '../components/ShortcutsOverlay'
import { GoNavigator } from '../components/GoNavigator'
import { NewItemDialog } from '../components/NewItemDialog'
import { NewKbDialog } from '../components/NewKbDialog'
import { NewMilestoneDialog } from '../components/NewMilestoneDialog'
import { NewProjectDocDialog } from '../components/NewProjectDocDialog'
import { ManageSlatesDialog } from '../components/ManageSlatesDialog'
import { JumpToRef } from '../components/JumpToRef'
import { ActivityDrawer } from '../components/ActivityDrawer'
import { ConnectionBanner } from '../components/ConnectionBanner'
import { SearchHighlightChip } from '../components/SearchHighlightChip'
import { BlockedDependentsDialog } from '../components/BlockedDependentsDialog'
import { Toasts } from '../components/Toasts'
import { GlobalShortcuts } from '../components/GlobalShortcuts'
import { Crosshair } from 'lucide-react'
import logoBright from '@logo/logo-bright.svg'
import logoDark from '@logo/logo-dark.svg'
import { useResolvedTheme, useThemeSync } from '../stores/theme'
import { useDialogs } from '../stores/dialogs'
import { useFormatVersionCheck } from '../lib/useFormatVersionCheck'
import { useNavigatorRepo } from '../stores/navigator'

function Header() {
  useThemeSync()
  useFormatVersionCheck()
  // Bind the navigator's filters to this repo app-wide, not only on /items:
  // the timeline reads the same store, and a restore that waited for the
  // items route would apply filters a screen late.
  useNavigatorRepo()
  const resolved = useResolvedTheme()
  const openJump = useDialogs((s) => s.openJump)
  return (
    // Three columns, the outer two equal, so the repo name sits at the true
    // centre however wide the search box or the chips on the right get.
    <header className="sticky top-0 z-30 grid grid-cols-[1fr_auto_1fr] items-center gap-4 px-4 py-2 border-b border-zinc-200 dark:border-zinc-800 bg-white/95 dark:bg-zinc-950/95 backdrop-blur">
      <div className="flex items-center gap-4 min-w-0">
        <Link
          to="/"
          aria-label="Knit home"
          title="Home"
          className="block shrink-0 rounded hover:opacity-80 focus:outline-none focus:ring-2 focus:ring-blue-500 leading-none"
        >
          <img
            src={resolved === 'dark' ? logoDark : logoBright}
            alt="Knit"
            width={24}
            height={24}
            className="block h-6 w-6"
          />
        </Link>
        <SearchBox />
        <SearchHighlightChip />
        <button
          type="button"
          onClick={openJump}
          title="Jump to id or ref (g r)"
          aria-label="Jump to id or ref"
          className="shrink-0 inline-flex items-center justify-center h-8 w-8 rounded-md text-zinc-600 dark:text-zinc-300 hover:bg-zinc-200/70 dark:hover:bg-zinc-800"
        >
          <Crosshair className="h-4 w-4" aria-hidden />
        </button>
      </div>
      <div className="hidden lg:flex justify-center min-w-0 max-w-sm">
        <RepoNameTitle />
      </div>
      <div className="flex items-center justify-end gap-4 min-w-0 col-start-3">
        <DoctorChip />
        <RepoIndicator />
        <IdentityChip />
        <HamburgerMenu />
      </div>
    </header>
  )
}

export const Route = createRootRoute({
  component: () => (
    <div className="h-screen overflow-hidden flex flex-col">
      <ConnectionBanner />
      <Header />
      <div className="flex-1 min-h-0 flex">
        <NavRail />
        <main className="flex-1 min-h-0 min-w-0">
          <Outlet />
        </main>
      </div>
      <ActivityDrawer />
      <ShortcutsOverlay />
      <GoNavigator />
      <GlobalShortcuts />
      <NewItemDialog />
      <NewKbDialog />
      <NewMilestoneDialog />
      <NewProjectDocDialog />
      <ManageSlatesDialog />
      <JumpToRef />
      <BlockedDependentsDialog />
      <Toasts />
    </div>
  ),
})

/**
 * Prevents accidental modal dismissal when text selection started inside
 * an input/textarea/modal element and mouseup/click finishes on the backdrop overlay.
 *
 * Dismissal only triggers when BOTH mousedown AND click originate directly
 * on the backdrop element itself (e.target === e.currentTarget).
 */
export function useModalBackdrop(defaultClose?: () => void) {
  let isBackdropTarget = false

  function onBackdropMouseDown(e: MouseEvent) {
    isBackdropTarget = e.target === e.currentTarget
  }

  function onBackdropMouseUp(e: MouseEvent) {
    if (e.target !== e.currentTarget) {
      isBackdropTarget = false
    }
  }

  function onBackdropClick(e: MouseEvent, callback?: () => void) {
    if (isBackdropTarget && e.target === e.currentTarget) {
      const fn = callback || defaultClose
      if (fn) {
        fn()
      }
    }
    isBackdropTarget = false
  }

  return {
    onBackdropMouseDown,
    onBackdropMouseUp,
    onBackdropClick
  }
}

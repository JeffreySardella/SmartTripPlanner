// Content script — injected on demand by the popup via chrome.scripting.executeScript
// Runs the parsing pipeline and returns extracted location data.
// Idempotency guard: skip if already injected (prevents duplicate listeners)
if (window.__smartTripPlannerInjected) {
  // Already injected — do nothing (listener is already registered)
} else {
  window.__smartTripPlannerInjected = true;

  function extractLocation() {
    // Tier 1: Site-specific parsers (highest quality)
    const siteResult =
      (typeof parseGoogleMaps === 'function' && parseGoogleMaps()) ||
      (typeof parseYelp === 'function' && parseYelp()) ||
      (typeof parseTripAdvisor === 'function' && parseTripAdvisor());

    if (siteResult && siteResult.name) {
      return { success: true, data: siteResult, source: 'site-parser' };
    }

    // Tier 2: Structured data (generic)
    const structuredResult =
      typeof parseStructuredData === 'function' && parseStructuredData();

    if (structuredResult && structuredResult.name) {
      return { success: true, data: structuredResult, source: 'structured-data' };
    }

    // Tier 3: LLM fallback — send raw page text
    const bodyText = (document.body.innerText || '').substring(0, 2000);
    if (bodyText.length > 50) {
      return {
        success: false,
        needsLlm: true,
        rawPageContent: bodyText,
        sourceUrl: window.location.href
      };
    }

    return { success: false, needsLlm: false };
  }

  // Listen for messages from popup
  chrome.runtime.onMessage.addListener((message, sender, sendResponse) => {
    if (message.action === 'extractLocation') {
      const result = extractLocation();
      result.sourceUrl = result.sourceUrl || window.location.href;
      sendResponse(result);
    }
    return true; // keep message channel open for async response
  });

} // end idempotency guard

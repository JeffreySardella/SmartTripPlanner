/**
 * Extracts location data from Google Maps pages.
 * Matches: google.com/maps, google.*/maps
 * @returns {{ name?: string, address?: string, latitude?: number, longitude?: number, category?: string } | null}
 */
function parseGoogleMaps() {
  const url = window.location.href;
  if (!url.includes('google.') || !url.includes('/maps')) return null;

  const result = {};

  // Extract coordinates from URL: @lat,lng,zoom
  const coordMatch = url.match(/@(-?\d+\.\d+),(-?\d+\.\d+)/);
  if (coordMatch) {
    result.latitude = parseFloat(coordMatch[1]);
    result.longitude = parseFloat(coordMatch[2]);
  }

  // Extract place name from URL: /place/Place+Name/
  const placeMatch = url.match(/\/place\/([^/@]+)/);
  if (placeMatch) {
    result.name = decodeURIComponent(placeMatch[1]).replace(/\+/g, ' ');
  }

  // Try DOM elements for name and address
  const nameEl = document.querySelector('h1.DUwDvf, h1[data-attrid="title"]');
  if (nameEl) result.name = nameEl.textContent.trim();

  const addressEl = document.querySelector('button[data-item-id="address"] div.Io6YTe');
  if (addressEl) result.address = addressEl.textContent.trim();

  const categoryEl = document.querySelector('button[jsaction*="category"] span');
  if (categoryEl) {
    const cat = categoryEl.textContent.trim().toLowerCase();
    result.category = mapGoogleCategory(cat);
  }

  return result.name ? result : null;
}

function mapGoogleCategory(googleCat) {
  const mapping = {
    'restaurant': 'restaurant', 'hotel': 'hotel', 'museum': 'museum',
    'park': 'park', 'bar': 'bar', 'cafe': 'cafe', 'store': 'shop',
    'shopping': 'shop', 'tourist attraction': 'attraction'
  };
  for (const [key, value] of Object.entries(mapping)) {
    if (googleCat.includes(key)) return value;
  }
  return 'other';
}

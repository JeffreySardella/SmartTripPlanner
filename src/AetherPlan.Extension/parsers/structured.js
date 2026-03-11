/**
 * Extracts location data from schema.org JSON-LD, Open Graph tags,
 * and other structured metadata on the page.
 * @returns {{ name?: string, address?: string, latitude?: number, longitude?: number, category?: string } | null}
 */
function parseStructuredData() {
  let result = {};

  // 1. Try schema.org JSON-LD
  const jsonLdScripts = document.querySelectorAll('script[type="application/ld+json"]');
  for (const script of jsonLdScripts) {
    try {
      const data = JSON.parse(script.textContent);
      const items = Array.isArray(data) ? data : [data];
      for (const item of items) {
        const extracted = extractFromSchemaOrg(item);
        if (extracted) {
          result = { ...result, ...extracted };
          break;
        }
      }
    } catch (e) { /* ignore malformed JSON-LD */ }
  }

  // 2. Try Open Graph meta tags
  if (!result.name) {
    const ogTitle = document.querySelector('meta[property="og:title"]');
    if (ogTitle) result.name = ogTitle.content;
  }

  // 3. Try geo meta tags
  if (!result.latitude) {
    const geoLat = document.querySelector('meta[name="geo.position"]');
    if (geoLat) {
      const parts = geoLat.content.split(';');
      if (parts.length === 2) {
        result.latitude = parseFloat(parts[0]);
        result.longitude = parseFloat(parts[1]);
      }
    }

    const icbmMeta = document.querySelector('meta[name="ICBM"]');
    if (icbmMeta && !result.latitude) {
      const parts = icbmMeta.content.split(',').map(s => s.trim());
      if (parts.length === 2) {
        result.latitude = parseFloat(parts[0]);
        result.longitude = parseFloat(parts[1]);
      }
    }
  }

  // 4. Try address elements
  if (!result.address) {
    const addressEl = document.querySelector('address');
    if (addressEl) result.address = addressEl.textContent.trim();
  }

  return result.name ? result : null;
}

function extractFromSchemaOrg(item) {
  const locationTypes = [
    'Restaurant', 'LocalBusiness', 'Place', 'Hotel', 'TouristAttraction',
    'FoodEstablishment', 'LodgingBusiness', 'Museum', 'Park', 'Store',
    'BarOrPub', 'CafeOrCoffeeShop'
  ];

  const itemType = item['@type'];
  if (!itemType) return null;

  const types = Array.isArray(itemType) ? itemType : [itemType];
  const isLocation = types.some(t => locationTypes.includes(t));
  if (!isLocation) return null;

  const result = {};
  result.name = item.name || null;

  if (item.address) {
    if (typeof item.address === 'string') {
      result.address = item.address;
    } else if (item.address.streetAddress) {
      const parts = [
        item.address.streetAddress,
        item.address.addressLocality,
        item.address.addressRegion,
        item.address.postalCode
      ].filter(Boolean);
      result.address = parts.join(', ');
    }
  }

  if (item.geo) {
    result.latitude = parseFloat(item.geo.latitude);
    result.longitude = parseFloat(item.geo.longitude);
  }

  // Map schema.org type to our category
  const typeMap = {
    'Restaurant': 'restaurant', 'FoodEstablishment': 'restaurant',
    'Hotel': 'hotel', 'LodgingBusiness': 'hotel',
    'TouristAttraction': 'attraction', 'Museum': 'museum',
    'Park': 'park', 'Store': 'shop', 'BarOrPub': 'bar',
    'CafeOrCoffeeShop': 'cafe'
  };

  for (const t of types) {
    if (typeMap[t]) { result.category = typeMap[t]; break; }
  }

  return result.name ? result : null;
}

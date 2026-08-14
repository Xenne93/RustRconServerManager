// Helper function to check if user has scrolled near the bottom of a container
export function isNearBottom(element, threshold = 100) {
    if (!element) {
        return false;
    }

    const scrollTop = element.scrollTop;
    const scrollHeight = element.scrollHeight;
    const clientHeight = element.clientHeight;

    // Check if we're within 'threshold' pixels from the bottom
    return (scrollHeight - scrollTop - clientHeight) < threshold;
}

/**
 * API Error class for handling standardized error responses
 */
class ApiError extends Error {
    constructor(code, message, details = null) {
        super(message);
        this.name = 'ApiError';
        this.code = code;
        this.details = details;
    }
}

/**
 * Handle API response with standardized error handling
 * @param {Object} response - ElectronAPI response object {ok, status, data} or Fetch API response
 * @returns {Promise<any>} - Response data
 * @throws {ApiError} - Standardized API error
 */
async function handleApiResponse(response) {
    // Handle ElectronAPI response format {ok, status, data}
    if (response.hasOwnProperty('data')) {
        if (!response.ok) {
            const errorData = response.data;
            
            // Check for standardized error format
            if (errorData && errorData.error && errorData.error.code) {
                throw new ApiError(
                    errorData.error.code,
                    errorData.error.message,
                    errorData.error.details
                );
            }
            
            // Fallback for non-standardized errors
            throw new ApiError(
                'UNKNOWN_ERROR',
                (errorData && (errorData.message || errorData.error)) || `HTTP ${response.status}`
            );
        }
        
        // Handle standardized success response { success: true, data: {...} }
        if (response.data && response.data.success === true) {
            return response.data.data;
        }
        
        // Fallback for non-standardized response
        return response.data;
    }
    
    // Handle standard Fetch API response
    if (!response.ok) {
        let errorData;
        try {
            errorData = await response.json();
        } catch (e) {
            // Failed to parse error response
            throw new ApiError('NETWORK_ERROR', response.statusText);
        }
        
        // Check for standardized error format
        if (errorData.error && errorData.error.code) {
            throw new ApiError(
                errorData.error.code,
                errorData.error.message,
                errorData.error.details
            );
        }
        
        // Fallback for non-standardized errors
        throw new ApiError(
            'UNKNOWN_ERROR',
            errorData.message || errorData.error || response.statusText
        );
    }
    
    const data = await response.json();
    
    // Handle standardized success response { success: true, data: {...} }
    if (data.success === true) {
        return data.data;
    }
    
    // Fallback for non-standardized response
    return data;
}

/**
 * Get error message for display
 * @param {Error} error - Error object
 * @param {object} i18n - i18n instance (optional)
 * @returns {string} - Error message
 */
function getErrorMessage(error, i18n = null) {
    if (error instanceof ApiError) {
        // Try to get i18n message if available
        if (i18n) {
            const key = `error.${error.code.toLowerCase()}`;
            const translated = i18n.t(key);
            if (translated !== key) {
                return translated;
            }
        }
        
        // Fallback to error message
        return error.message;
    }
    
    return error.message || 'An unknown error occurred';
}

// Export as ES6 modules
export { ApiError, handleApiResponse, getErrorMessage };

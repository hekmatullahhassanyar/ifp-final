#include "deliveryfee.h"

#define DQ_MAX_AMOUNT_CENTS INT64_C(90000000000000000)

int64_t dq_apply_discount(int64_t amount_cents, int32_t percent)
{
    if (amount_cents < 0 || percent < 0 || percent > 100) {
        return DQ_ERR_INVALID_ARGUMENT;
    }
    if (amount_cents > DQ_MAX_AMOUNT_CENTS) {
        return DQ_ERR_OVERFLOW;
    }
    
    return (amount_cents * (int64_t)(100 - percent) + 50) / 100;
}

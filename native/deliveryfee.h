#ifndef DELIVERYFEE_H
#define DELIVERYFEE_H

#include <stdint.h>

#ifdef _WIN32
#define DQ_API __declspec(dllexport)
#else
#define DQ_API __attribute__((visibility("default")))
#endif

#define DQ_ERR_INVALID_ARGUMENT (-1)
#define DQ_ERR_OVERFLOW         (-2)

DQ_API int64_t dq_apply_discount(int64_t amount_cents, int32_t percent);

#endif

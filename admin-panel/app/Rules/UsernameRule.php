<?php

namespace App\Rules;

use Closure;
use Illuminate\Contracts\Validation\ValidationRule;

final class UsernameRule implements ValidationRule
{
    public function validate(string $attribute, mixed $value, Closure $fail): void
    {
        $message = AccountRules::username($value);
        if ($message !== null) {
            $fail($message);
        }
    }
}

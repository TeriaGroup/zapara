<?php

namespace App\Rules;

use Closure;
use Illuminate\Contracts\Validation\ValidationRule;

final class PasswordRule implements ValidationRule
{
    public function __construct(private readonly bool $required = true) {}

    public function validate(string $attribute, mixed $value, Closure $fail): void
    {
        $message = AccountRules::password($value, $this->required);
        if ($message !== null) {
            $fail($message);
        }
    }
}
